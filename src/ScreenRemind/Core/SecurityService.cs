using System.Security.Cryptography;
using System.Text;

namespace ScreenRemind.Core;

/// <summary>
/// 主界面密码（O-11）：PBKDF2-SHA256 加盐存储。
/// 明确边界：只锁界面，不加密 shots 里的 PNG —— 文档里也会这么写，不夸大。
/// </summary>
public static class SecurityService
{
    private const int Iterations = 120_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    public static (string hash, string salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hashBase64, string saltBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
