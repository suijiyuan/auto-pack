using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoPack.Core;

public sealed record PackRequest(IReadOnlyList<string> Files, string OutputDirectory, string Password, long VolumeBytes = 734003200);
public sealed record PackProgress(string Stage, int Percent);
public sealed record PackResult(string Folder, IReadOnlyList<string> Volumes);

public sealed class PackService
{
    private readonly string enginePath;
    public PackService(string enginePath) => this.enginePath = Path.GetFullPath(enginePath);

    public async Task<PackResult> RunAsync(PackRequest request, IProgress<PackProgress>? progress, CancellationToken token)
    {
        SettingsStore.Validate(new AppSettings(request.OutputDirectory, request.Password));
        if (request.VolumeBytes < 1024) throw new ArgumentException("分卷大小必须至少为 1024 字节。");
        var files = ValidateFiles(request.Files);
        if (!File.Exists(enginePath)) throw new FileNotFoundException("缺少 7-Zip 压缩引擎，请完整解压应用发布包。", enginePath);
        token.ThrowIfCancellationRequested();
        var date = DateOnly.FromDateTime(DateTime.Now);
        var output = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(output);
        progress?.Report(new PackProgress("准备输出目录", 0));
        using var outputLock = await AcquireLockAsync(output, token);
        var name = NextName(output, date);
        var staging = Path.Combine(output, ".autopack-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(staging, name);
        var published = new List<string>();
        string? publishedFolder = null;
        try
        {
            Directory.CreateDirectory(folder);
            var total = files.Sum(p => new FileInfo(p).Length);
            long copied = 0;
            foreach (var source in files)
            {
                token.ThrowIfCancellationRequested();
                var before = new FileInfo(source);
                var length = before.Length;
                var modified = before.LastWriteTimeUtc;
                // Windows 下 FileShare.Read 阻止任务复制期间其他进程写入或删除源文件。
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                using (var target = new FileStream(Path.Combine(folder, Path.GetFileName(source)), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                {
                    var buffer = new byte[1024 * 1024];
                    int count;
                    while ((count = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, count), token);
                        copied += count;
                        progress?.Report(new PackProgress("正在复制：" + Path.GetFileName(source), total == 0 ? 25 : (int)(copied * 25.0 / total)));
                    }
                    if (input.Length != length) throw new IOException("复制期间源文件发生变化，请关闭正在编辑该文件的程序后重试。");
                }
                var after = new FileInfo(source);
                if (after.Length != length || after.LastWriteTimeUtc != modified)
                    throw new IOException("复制期间源文件发生变化，请关闭正在编辑该文件的程序后重试。");
                File.SetLastWriteTimeUtc(Path.Combine(folder, Path.GetFileName(source)), modified);
            }

            var archive = Path.Combine(staging, name + ".7z");
            var runner = new SevenZipRunner(enginePath);
            await runner.RunAsync(staging,
                ["a", "-t7z", "-m0=LZMA2", "-mx=5", "-mhe=on", "-p", "-v" + request.VolumeBytes.ToString(CultureInfo.InvariantCulture) + "b", "-sccUTF-8", "-bsp1", "-bso0", "-bse2", "-y", archive, "--", name],
                request.Password, "正在压缩", 25, 85, progress, token);
            var volumes = Directory.GetFiles(staging, name + ".7z.*").Order(StringComparer.Ordinal).ToArray();
            if (volumes.Length == 0 || !volumes[0].EndsWith(".001", StringComparison.Ordinal))
                throw new IOException("压缩引擎未生成预期的首个分卷。");
            if (volumes.Any(p => new FileInfo(p).Length is <= 0 || new FileInfo(p).Length > request.VolumeBytes))
                throw new IOException("分卷大小校验失败。");
            // 测试已加密文件头会自动要求密码；这里不能传空值 -p，否则会被解释为空密码。
            await runner.RunAsync(staging, ["t", "-sccUTF-8", "-bsp1", "-bso0", "-bse2", volumes[0]], request.Password, "正在校验", 85, 98, progress, token);
            token.ThrowIfCancellationRequested();
            progress?.Report(new PackProgress("正在保存结果", 99));
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(output, name);
            Directory.Move(folder, destination);
            publishedFolder = destination;
            foreach (var volume in volumes)
            {
                var target = Path.Combine(output, Path.GetFileName(volume));
                File.Move(volume, target); // 不允许覆盖外部文件。
                published.Add(target);
            }
            Directory.Delete(staging);
            progress?.Report(new PackProgress("打包完成", 100));
            return new PackResult(destination, published);
        }
        catch (Exception original)
        {
            var leftovers = new List<string>();
            foreach (var file in published)
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { leftovers.Add(file); }
            foreach (var directory in new[] { publishedFolder, staging })
                if (directory is not null)
                    try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { leftovers.Add(directory); }
            if (leftovers.Count > 0)
                throw new IOException("任务未完成，以下本次任务产物无法清理，请手动检查：\n" + string.Join("\n", leftovers), original);
            throw;
        }
    }

    private static async Task<FileStream> AcquireLockAsync(string output, CancellationToken token)
    {
        var path = Path.Combine(output, ".autopack.lock");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 or 11 or 35)
            { await Task.Delay(200, token); }
        }
    }

    public static string NextName(string output, DateOnly date)
    {
        var prefix = "文件传输-" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-";
        var pattern = new Regex("^" + Regex.Escape(prefix) + @"([0-9]+)(?:\.7z(?:\.[0-9]+)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        long max = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(output))
        {
            var match = pattern.Match(Path.GetFileName(entry));
            if (!match.Success) continue;
            if (!long.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var number) || number == long.MaxValue)
                throw new IOException("输出目录中存在超出范围的任务序号，请先检查文件名称。");
            max = Math.Max(max, number);
        }
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<string> ValidateFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) throw new ArgumentException("请先添加需要打包的文件。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var path in files)
        {
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            if (!names.Add(name)) throw new ArgumentException($"存在同名或重复文件：{name}。请先移除冲突项。");
            var attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new ArgumentException($"只支持普通文件，不支持文件夹或链接：{name}");
            result.Add(full);
        }
        return result;
    }
}
