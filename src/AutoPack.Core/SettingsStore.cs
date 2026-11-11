using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoPack.Core;

public enum AppTheme { Light, Dark, System }
public sealed record AppSettings(string OutputDirectory, string Password, AppTheme Theme = AppTheme.Light);
public interface ISecretProtector
{
    byte[] Protect(byte[] value);
    byte[] Unprotect(byte[] value);
}
public sealed class SettingsStore(string path, ISecretProtector protector)
{
    private sealed record StoredSettings(int Version, string OutputDirectory, string ProtectedPassword, AppTheme Theme = AppTheme.Light);

    public AppSettings? Load()
    {
        if (!File.Exists(path)) return null;
        byte[]? plain = null;
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path));
            if (stored is null || stored.Version != 1 || string.IsNullOrEmpty(stored.ProtectedPassword))
                throw new InvalidDataException();
            plain = protector.Unprotect(Convert.FromBase64String(stored.ProtectedPassword));
            var settings = new AppSettings(stored.OutputDirectory, new UTF8Encoding(false, true).GetString(plain), stored.Theme);
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or CryptographicException or InvalidDataException)
        {
            throw new InvalidDataException("无法读取设置或解密密码。请在设置中重新配置；原配置尚未更改。", ex);
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        var plain = Encoding.UTF8.GetBytes(settings.Password);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var stored = new StoredSettings(1, Path.GetFullPath(settings.OutputDirectory), Convert.ToBase64String(protector.Protect(plain)), settings.Theme);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, stored, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)) throw new ArgumentException("请选择浅色、深色或跟随系统主题。");
        if (string.IsNullOrWhiteSpace(settings.OutputDirectory) || !Path.IsPathFullyQualified(settings.OutputDirectory))
            throw new ArgumentException("请选择有效的绝对输出路径。");
        _ = Path.GetFullPath(settings.OutputDirectory);
        if (string.IsNullOrWhiteSpace(settings.Password)) throw new ArgumentException("请设置非空的加密密码。");
        if (settings.Password.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("密码不能包含换行或空字符。");
    }
}
