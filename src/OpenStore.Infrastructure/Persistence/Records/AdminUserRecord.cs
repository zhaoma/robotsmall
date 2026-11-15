namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AdminUser</c> 表的持久化模型（与领域聚合 <see cref="Domain.Customers.AdminUser"/> 分离）。
/// </summary>
public sealed class AdminUserRecord
{
    public Guid Id { get; set; }

    /// <summary>原样大小写，仅展示用。</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>规范化邮箱（Trim + 小写），唯一索引与查找都基于它。</summary>
    public string EmailNormalized { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>存 <see cref="Domain.Customers.AdminRole"/> 的数值。</summary>
    public int Role { get; set; }

    /// <summary>存 <see cref="Domain.Customers.AdminUserStatus"/> 的数值。</summary>
    public int Status { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    /// <summary>乐观锁版本号（M0 仅落库，不做并发冲突检测——登录/改密低频，见后端设计 §0）。</summary>
    public long Version { get; set; }
}
