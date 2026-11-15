namespace OpenStore.Application.Abstractions;

/// <summary>
/// 密码哈希端口。实现放在 Infrastructure（PBKDF2-SHA256，默认 100k 迭代）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是端口而不是直接 new <c>PasswordHasher&lt;T&gt;</c></b>：Application 层不允许引用
/// 任何第三方包（门禁 Category=Layering 会扫），而 ASP.NET Identity 的 <c>PasswordHasher</c>
/// 属于框架包。把它挡在端口后面还有一个附带收益：哈希算法可换（PBKDF2 → Argon2）
/// 而领域与应用代码一行不改。
/// </para>
/// <para>
/// <b>Verify 必须对"账号不存在"也跑一次等成本的计算</b>：调用方拿不到账号时，
/// 仍应调用 <see cref="Verify"/> 并传入一个固定的假哈希，否则"账号存在但密码错"与
/// "账号不存在"的响应时间差会被用来枚举账号（后端设计 §6.3）。
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>生成可存库的哈希字符串（自带盐与参数，格式由实现决定）。</summary>
    string Hash(string password);

    /// <summary>校验密码。哈希格式非法时返回 <c>false</c>，<b>不抛异常</b>（脏数据不该变成 500）。</summary>
    bool Verify(string passwordHash, string password);
}
