using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoPack.Core;

internal sealed class SevenZipRunner(string enginePath)
{
    public async Task RunAsync(string workingDirectory, IReadOnlyList<string> arguments, string password, string stage,
        int startPercent, int endPercent, IProgress<PackProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(enginePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var cancellation = token.Register(() => Stop(process));
        var stdout = DrainAsync(process.StandardOutput, stage, startPercent, endPercent, progress);
        var stderr = DrainAsync(process.StandardError, stage, startPercent, endPercent, null);
        try
        {
            progress?.Report(new PackProgress(stage, startPercent));
            token.ThrowIfCancellationRequested();
            // 输入两行兼容要求确认密码的版本；测试/解压仅消费首行。
            await process.StandardInput.WriteLineAsync(password);
            await process.StandardInput.WriteLineAsync(password);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new IOException($"7-Zip {stage}失败（退出码 {process.ExitCode}）。请检查磁盘空间、文件权限和压缩引擎是否完整。");
        }
        catch
        {
            Stop(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task DrainAsync(StreamReader reader, string stage, int from, int to, IProgress<PackProgress>? progress)
    {
        var buffer = new char[2048];
        var tail = "";
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            if (progress is null) continue;
            var text = tail + new string(buffer, 0, count);
            foreach (Match match in Regex.Matches(text, @"(?<![0-9])([0-9]{1,3})%"))
            {
                var percent = Math.Clamp(int.Parse(match.Groups[1].Value), 0, 100);
                progress.Report(new PackProgress(stage, from + (to - from) * percent / 100));
            }
            tail = text.Length > 4 ? text[^4..] : text;
        }
    }
}
