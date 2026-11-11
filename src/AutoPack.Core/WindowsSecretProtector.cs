using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AutoPack.Core;

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] value) => Transform(value, protect: true);
    public byte[] Unprotect(byte[] value) => Transform(value, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("密码保护需要 Windows。");
        var input = new DataBlob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            // 默认范围为当前用户，禁止操作系统弹出交互式凭据窗口。
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("Windows 无法保护或恢复密码。", new Win32Exception(Marshal.GetLastWin32Error()));
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
