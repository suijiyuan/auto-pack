using AutoPack.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var failures = 0;
await Test("已有文件夹和分卷均参与日期序号计算", () =>
{
    using var temp = new TempDirectory();
    Directory.CreateDirectory(Path.Combine(temp.Path, "文件传输-20261111-002"));
    File.WriteAllText(Path.Combine(temp.Path, "文件传输-20261111-007.7z.001"), "existing");
    Equal("文件传输-20261111-008", PackService.NextName(temp.Path, new DateOnly(2026, 11, 11)));
    Equal("文件传输-20261112-001", PackService.NextName(temp.Path, new DateOnly(2026, 11, 12)));
    Equal("文件传输-20260102-001", PackService.NextName(temp.Path, new DateOnly(2026, 1, 2)));
    return Task.CompletedTask;
});
await Test("拒绝 Windows 同名文件和重复选择", () =>
{
    using var temp = new TempDirectory();
    Directory.CreateDirectory(Path.Combine(temp.Path, "a"));
    Directory.CreateDirectory(Path.Combine(temp.Path, "b"));
    var a = Path.Combine(temp.Path, "a", "合同.txt");
    var b = Path.Combine(temp.Path, "b", "合同.TXT");
    File.WriteAllText(a, "one"); File.WriteAllText(b, "two");
    Throws<ArgumentException>(() => PackService.ValidateFiles([a, b]));
    Throws<ArgumentException>(() => PackService.ValidateFiles([a, a]));
    Throws<ArgumentException>(() => PackService.ValidateFiles([]));
    return Task.CompletedTask;
});
await Test("设置重启恢复且配置没有明文密码", () =>
{
    using var temp = new TempDirectory();
    var path = Path.Combine(temp.Path, "settings.json");
    var store = new SettingsStore(path, new TestProtector());
    Equal<AppSettings?>(null, store.Load());
    store.Save(new AppSettings(temp.Path, "中文 密码!42"));
    var restored = new SettingsStore(path, new TestProtector()).Load()!;
    Equal(temp.Path, restored.OutputDirectory);
    Equal("中文 密码!42", restored.Password);
    Assert(!File.ReadAllText(path).Contains("中文 密码!42"), "配置泄露密码");
    var original = File.ReadAllText(path);
    Throws<ArgumentException>(() => store.Save(new AppSettings(temp.Path, "bad\npassword")));
    Equal(original, File.ReadAllText(path));
    File.WriteAllText(path, "broken json");
    Throws<InvalidDataException>(() => store.Load());
    return Task.CompletedTask;
});
await Test("浅色、深色和跟随系统主题保存后可恢复且修改主题保留密码", () =>
{
    using var temp = new TempDirectory();
    var path = Path.Combine(temp.Path, "settings.json");
    var store = new SettingsStore(path, new TestProtector());
    foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.System })
    {
        store.Save(new AppSettings(temp.Path, "原密码", theme));
        var restored = new SettingsStore(path, new TestProtector()).Load()!;
        Equal(theme, restored.Theme);
        var password = PasswordInput.Resolve("", "", false, restored.Password);
        store.Save(new AppSettings(temp.Path, password, theme));
        Equal("原密码", store.Load()!.Password);
        Assert(!File.ReadAllText(path).Contains("原密码"), "修改主题后泄露密码");
    }
    return Task.CompletedTask;
});
await Test("旧配置没有主题字段时默认浅色且保留目录和密码", () =>
{
    using var temp = new TempDirectory();
    var path = Path.Combine(temp.Path, "settings.json");
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 1, OutputDirectory = temp.Path,
        ProtectedPassword = Convert.ToBase64String(new TestProtector().Protect(Encoding.UTF8.GetBytes("旧密码")))
    }));
    var restored = new SettingsStore(path, new TestProtector()).Load()!;
    Equal(AppTheme.Light, restored.Theme);
    Equal(temp.Path, restored.OutputDirectory);
    Equal("旧密码", restored.Password);
    return Task.CompletedTask;
});
await Test("无效主题不能覆盖原配置且损坏主题不会静默重置", () =>
{
    using var temp = new TempDirectory();
    var path = Path.Combine(temp.Path, "settings.json");
    var store = new SettingsStore(path, new TestProtector());
    store.Save(new AppSettings(temp.Path, "原密码", AppTheme.Dark));
    var original = File.ReadAllText(path);
    Throws<ArgumentException>(() => store.Save(new AppSettings(temp.Path, "原密码", (AppTheme)99)));
    Equal(original, File.ReadAllText(path));
    var json = System.Text.Json.Nodes.JsonNode.Parse(original)!;
    foreach (var invalid in new[] { "99", "null", "\"unknown\"" })
    {
        json["Theme"] = System.Text.Json.Nodes.JsonNode.Parse(invalid);
        File.WriteAllText(path, json.ToJsonString());
        var corrupted = File.ReadAllText(path);
        Throws<InvalidDataException>(() => store.Load());
        Equal(corrupted, File.ReadAllText(path));
    }
    return Task.CompletedTask;
});
await Test("拒绝空密码及交互控制字符", () =>
{
    foreach (var password in new[] { "", " ", "a\nb", "a\rb", "a\0b" })
        Throws<ArgumentException>(() => SettingsStore.Validate(new AppSettings(Path.GetTempPath(), password)));
    return Task.CompletedTask;
});
await Test("显示密码时单次输入即可保存且忽略旧确认内容", () =>
{
    using var temp = new TempDirectory();
    var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"), new TestProtector());
    store.Save(new AppSettings(temp.Path, PasswordInput.Resolve("中文 新密码!42", "", true)));
    Equal("中文 新密码!42", store.Load()!.Password);
    store.Save(new AppSettings(temp.Path, PasswordInput.Resolve("另一个密码", "旧确认内容", true, store.Load()!.Password)));
    Equal("另一个密码", store.Load()!.Password);
    return Task.CompletedTask;
});
await Test("隐藏密码时必须确认一致且留空可保留原密码", () =>
{
    Equal("新密码", PasswordInput.Resolve("新密码", "新密码", false));
    Throws<ArgumentException>(() => PasswordInput.Resolve("新密码", "", false));
    Throws<ArgumentException>(() => PasswordInput.Resolve("新密码", "不同密码", false, "原密码"));
    Equal("原密码", PasswordInput.Resolve("", "", false, "原密码"));
    Throws<ArgumentException>(() => PasswordInput.Resolve("", "残留确认", false, "原密码"));
    return Task.CompletedTask;
});
await Test("显示密码时留空保留原密码且首次设置仍拒绝无效密码", () =>
{
    using var temp = new TempDirectory();
    var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"), new TestProtector());
    store.Save(new AppSettings(temp.Path, "原密码"));
    var previous = store.Load()!.Password;
    store.Save(new AppSettings(temp.Path, PasswordInput.Resolve("", "残留确认", true, previous)));
    Equal("原密码", store.Load()!.Password);
    foreach (var value in new[] { "", " ", "a\nb", "a\rb", "a\0b" })
        Throws<ArgumentException>(() => store.Save(new AppSettings(temp.Path, PasswordInput.Resolve(value, "", true))));
    Equal("原密码", store.Load()!.Password);
    return Task.CompletedTask;
});
if (OperatingSystem.IsWindows())
{
    await Test("真实 Windows DPAPI 保存和恢复密码并拒绝篡改", () =>
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var protector = new WindowsSecretProtector();
        var password = "长期使用的 中文 密码!#$2026";
        new SettingsStore(path, protector).Save(new AppSettings(temp.Path, password));
        Equal(password, new SettingsStore(path, new WindowsSecretProtector()).Load()!.Password);
        Assert(!File.ReadAllText(path).Contains(password), "泄露明文");
        var encrypted = protector.Protect(Encoding.UTF8.GetBytes(password));
        encrypted[encrypted.Length / 2] ^= 0xff;
        Throws<CryptographicException>(() => { if (OperatingSystem.IsWindows()) protector.Unprotect(encrypted); });
        return Task.CompletedTask;
    });
}
else Console.WriteLine("SKIP 真实 Windows DPAPI（当前操作系统非 Windows）");

var engine = Environment.GetEnvironmentVariable("AUTO_PACK_7ZIP");
if (string.IsNullOrEmpty(engine))
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    engine = Path.Combine(root, OperatingSystem.IsWindows() ? "tools/7zip/7za.exe" : ".cache/7zip/7zz");
}
if (!File.Exists(engine)) throw new FileNotFoundException("请设置 AUTO_PACK_7ZIP 为真实 7-Zip 引擎路径后运行集成测试。", engine);
engine = Path.GetFullPath(engine);

await Test("真实加密多卷归档可恢复且隐藏文件名", async () =>
{
    using var temp = new TempDirectory();
    var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var lockPath = Path.Combine(output, ".autopack.lock");
    File.WriteAllText(lockPath, ""); // 兼容旧版本遗留的空锁文件。
    var source = Path.Combine(sourceDir, "中文 合同 [最终] &.bin");
    var empty = Path.Combine(sourceDir, "空文件.txt");
    var data = RandomNumberGenerator.GetBytes(180000);
    File.WriteAllBytes(source, data); File.WriteAllBytes(empty, []);
    var password = "中文 !#$& quote\"' 密码😀";
    var result = await new PackService(engine).RunAsync(new PackRequest([source, empty], output, password, 65536), null, CancellationToken.None);
    Assert(!File.Exists(lockPath), "完成后残留锁文件");
    Assert(result.Volumes.Count >= 3, "未生成多个分卷");
    Assert(result.Volumes.All(p => new FileInfo(p).Length <= 65536), "分卷超出大小上限");
    Assert(result.Volumes[0].EndsWith(".7z.001"), "首卷命名错误");
    Assert(data.SequenceEqual(File.ReadAllBytes(source)), "原文件被修改");
    Assert(data.SequenceEqual(File.ReadAllBytes(Path.Combine(result.Folder, Path.GetFileName(source)))), "整理文件错误");
    var extracted = Path.Combine(temp.Path, "restore");
    var restored = await RunEngine(["x", "-y", "-sccUTF-8", "-o" + extracted, result.Volumes[0]], password);
    Equal(0, restored.Code);
    var recovered = Path.Combine(extracted, Path.GetFileName(result.Folder), Path.GetFileName(source));
    Assert(data.SequenceEqual(File.ReadAllBytes(recovered)), "解压内容不一致");
    Equal(0L, new FileInfo(Path.Combine(extracted, Path.GetFileName(result.Folder), "空文件.txt")).Length);
    var wrong = await RunEngine(["t", "-sccUTF-8", result.Volumes[0]], "wrong password");
    Assert(wrong.Code != 0, "错误密码被接受");
    var listing = await RunEngine(["l", "-sccUTF-8", result.Volumes[0]], "");
    Assert(listing.Code != 0 && !listing.Output.Contains(Path.GetFileName(source)), "无密码泄露文件名");
    var second = await new PackService(engine).RunAsync(new PackRequest([empty], output, password), null, CancellationToken.None);
    Assert(Path.GetFileName(second.Folder).EndsWith("-002"), "后续任务未递增序号");
    Assert(!File.Exists(lockPath), "再次打包后残留锁文件");
});
await Test("压缩进程取消后清理本次任务且保留既有文件", async () =>
{
    using var temp = new TempDirectory();
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var existing = Path.Combine(output, "保留.txt"); File.WriteAllText(existing, "keep");
    var source = Path.Combine(temp.Path, "source.bin"); File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(2000000));
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var progress = new InlineProgress(p => { if (p.Stage == "正在压缩") cancel.Cancel(); });
    try
    {
        await new PackService(engine).RunAsync(new PackRequest([source], output, "cancel test"), progress, cancel.Token);
        throw new Exception("取消未生效");
    }
    catch (OperationCanceledException) { }
    Assert(File.Exists(source), "源文件被删除");
    Equal("keep", File.ReadAllText(existing));
    Assert(Directory.EnumerateFileSystemEntries(output).All(p => p == existing), "取消后存在残留任务或锁文件");
});
await Test("失败不会删除源文件或已有任务", async () =>
{
    using var temp = new TempDirectory();
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var source = Path.Combine(temp.Path, "source.txt"); File.WriteAllText(source, "source");
    var existing = Path.Combine(output, "existing.7z.001"); File.WriteAllText(existing, "keep");
    try
    {
        await new PackService(Path.Combine(temp.Path, "missing-engine.exe")).RunAsync(new PackRequest([source], output, "password"), null, CancellationToken.None);
        throw new Exception("缺少引擎时未失败");
    }
    catch (FileNotFoundException) { }
    Equal("source", File.ReadAllText(source)); Equal("keep", File.ReadAllText(existing));
    Assert(!Directory.EnumerateDirectories(output).Any(), "失败后残留目录");
});
await Test("发布时发生外部重名冲突不会覆盖或清理外部文件", async () =>
{
    using var temp = new TempDirectory();
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var source = Path.Combine(temp.Path, "source.txt"); File.WriteAllText(source, "source");
    var name = PackService.NextName(output, DateOnly.FromDateTime(DateTime.Now));
    var collision = Path.Combine(output, name + ".7z.001");
    var progress = new InlineProgress(p => { if (p.Stage == "正在保存结果") File.WriteAllText(collision, "external"); });
    try
    {
        await new PackService(engine).RunAsync(new PackRequest([source], output, "password"), progress, CancellationToken.None);
        throw new Exception("未拒绝外部冲突");
    }
    catch (IOException) { }
    Equal("external", File.ReadAllText(collision));
    Equal("source", File.ReadAllText(source));
    Assert(!Directory.EnumerateDirectories(output).Any(), "失败任务目录未清理");
    Assert(!File.Exists(Path.Combine(output, ".autopack.lock")), "失败后残留锁文件");
});
await Test("并发任务在同一输出目录获得不同序号", async () =>
{
    using var temp = new TempDirectory();
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var source = Path.Combine(temp.Path, "source.txt"); File.WriteAllText(source, "source");
    var request = new PackRequest([source], output, "password");
    var results = await Task.WhenAll(
        new PackService(engine).RunAsync(request, null, CancellationToken.None),
        new PackService(engine).RunAsync(request, null, CancellationToken.None));
    Assert(results.Select(r => r.Folder).Distinct().Count() == 2, "并发目录重名");
    Assert(results.Any(r => r.Folder.EndsWith("-001")) && results.Any(r => r.Folder.EndsWith("-002")), "并发序号错误");
    Assert(!File.Exists(Path.Combine(output, ".autopack.lock")), "并发任务全部完成后残留锁文件");
});
await Test("等待锁的任务取消时不会删除其他任务正在使用的锁", async () =>
{
    using var temp = new TempDirectory();
    var output = Directory.CreateDirectory(Path.Combine(temp.Path, "output")).FullName;
    var source = Path.Combine(temp.Path, "source.txt"); File.WriteAllText(source, "source");
    var lockPath = Path.Combine(output, ".autopack.lock");
    using (var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = new PackService(engine).RunAsync(new PackRequest([source], output, "password"), null, cancel.Token);
        Assert(!waiting.IsCompleted, "其他任务未等待正在使用的锁");
        cancel.Cancel();
        try { await waiting; throw new Exception("等待锁时取消未生效"); }
        catch (OperationCanceledException) { }
        Assert(File.Exists(lockPath), "等待者删除了其他任务正在使用的锁");
        Assert(!Directory.EnumerateDirectories(output).Any(), "等待锁时创建了任务目录");
    }
    File.Delete(lockPath);
});
await Test("保护密码失败保留原配置", () =>
{
    using var temp = new TempDirectory();
    var path = Path.Combine(temp.Path, "settings.json");
    new SettingsStore(path, new TestProtector()).Save(new AppSettings(temp.Path, "old password", AppTheme.Dark));
    var original = File.ReadAllText(path);
    Throws<CryptographicException>(() => new SettingsStore(path, new FailingProtector()).Save(new AppSettings(temp.Path, "new password", AppTheme.Light)));
    Equal(original, File.ReadAllText(path));
    Equal(AppTheme.Dark, new SettingsStore(path, new TestProtector()).Load()!.Theme);
    Assert(Directory.GetFiles(temp.Path).Length == 1, "残留临时配置");
    return Task.CompletedTask;
});

Console.WriteLine($"测试结束：{failures} 项失败");
return failures == 0 ? 0 : 1;

async Task<(int Code, string Output)> RunEngine(string[] arguments, string password)
{
    var start = new ProcessStartInfo(engine) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardInputEncoding = new UTF8Encoding(false), CreateNoWindow = true };
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteLineAsync(password); await process.StandardInput.WriteLineAsync(password);
    process.StandardInput.Close();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { if (!process.HasExited) process.Kill(true); throw; }
    return (process.ExitCode, await stdout + await stderr);
}

async Task Test(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"期望 {expected}，实际 {actual}");
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"未抛出 {typeof(T).Name}");
}
sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "autopack-test-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
// 仅替代当前操作系统不提供的 DPAPI；Windows 专项另测真实 DPAPI。
sealed class TestProtector : ISecretProtector
{
    public byte[] Protect(byte[] value) => value.Select(b => (byte)(b ^ 0x5a)).ToArray();
    public byte[] Unprotect(byte[] value) => Protect(value);
}
sealed class InlineProgress(Action<PackProgress> callback) : IProgress<PackProgress>
{
    public void Report(PackProgress progress) => callback(progress);
}
sealed class FailingProtector : ISecretProtector
{
    public byte[] Protect(byte[] value) => throw new CryptographicException("模拟密码保护失败");
    public byte[] Unprotect(byte[] value) => throw new CryptographicException("模拟解密失败");
}
