namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>Session</c> 表的持久化模型（与领域实体 <see cref="Domain.Customers.Session"/> 分离）。
/// </summary>
/// <remarks>
/// <c>ActorId</c> 是多态引用（Admin / Customer），M0 只有 Admin 会话，故不建 FK：
/// 主体状态由认证处理器在同一请求里另查 <c>AdminUser</c>，避免会话表冗余角色/状态（见 <c>Session.cs</c> 注释）。
/// </remarks>
public sealed class SessionRecord
{
    /// <summary><c>SHA-256(rawToken)</c> 的小写 hex（64 字符），主键。</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>存 <see cref="Domain.Customers.SessionActorType"/> 的数值。</summary>
    public int ActorType { get; set; }

    public Guid ActorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset IdleExpiresAt { get; set; }

    public DateTimeOffset AbsoluteExpiresAt { get; set; }

    public string? IpHash { get; set; }

    public string? UserAgentHash { get; set; }

    public string? CsrfHash { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokeReason { get; set; }
}
