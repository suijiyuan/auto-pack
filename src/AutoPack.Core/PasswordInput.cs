namespace AutoPack.Core;

public static class PasswordInput
{
    public static string Resolve(string password, string confirmation, bool isVisible, string? currentPassword = null)
    {
        if (currentPassword is not null && password.Length == 0 && (isVisible || confirmation.Length == 0))
            return currentPassword;
        if (!isVisible && password != confirmation)
            throw new ArgumentException("两次输入的密码不一致。");
        return password;
    }
}
