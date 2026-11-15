using System.Security.Cryptography;
using OpenStore.Application.Abstractions;

namespace OpenStore.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 密码哈希（后端设计 §6.7：默认 100k 迭代，不引整套 Identity）。
/// </summary>
/// <remarks>
/// 存储格式：<c>PBKDF2-SHA256$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c>，
/// 自带盐与迭代参数，未来升级迭代次数时旧哈希仍可校验（<c>Verify</c> 从哈希里读参数）。
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string passwordHash, string password)
    {
        // 脏数据（空/畸形哈希）返回 false，不抛异常——不该让一条坏记录变成登录 500
        if (string.IsNullOrWhiteSpace(passwordHash) || password is null)
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
