using System.Security.Cryptography;
using System.Text;

namespace OpenStore.Application.Customers;

/// <summary>
/// 会话令牌工具：生成 32 字节随机 base64url 作为 Cookie 值，DB 只存它的 SHA-256 hex。
/// </summary>
public static class SessionToken
{
    /// <summary>生成 Cookie 值（32 字节随机，base64url）。</summary>
    public static string Generate()
        => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>计算 <c>SHA-256(token)</c> 的小写 hex（64 字符），作为会话主键。</summary>
    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
