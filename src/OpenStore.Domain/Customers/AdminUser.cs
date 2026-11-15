using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Customers;

/// <summary>
/// 管理员角色。<b>枚举而非角色表</b>（数据模型 §3.6）：需求把角色固定为两种且不提供自定义角色，
/// 建 <c>AdminRole</c> + <c>RolePermission</c> 表会让"细粒度 RBAC"看起来随时可开，最终被要求实现。
/// 角色 → 权限点的映射写成代码常量（见 Application <c>Permissions.ForRole</c>）。
/// </summary>
public enum AdminRole
{
    Admin = 0,
    Staff = 1,
}

/// <summary>管理员账号状态。</summary>
public enum AdminUserStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>
/// 管理员账号聚合。
/// </summary>
/// <remarks>
/// <para>
/// <b>密码哈希的验算不在本类</b>：<c>PasswordHasher</c> 属于基础设施（PBKDF2 实现细节不应进领域层），
/// 本类只持有不透明字符串并提供"记录一次失败/成功"的状态迁移。这样领域层零依赖，
/// 且换哈希算法（PBKDF2 → Argon2）时聚合完全不用改。
/// </para>
/// <para>
/// <b>为什么锁定逻辑在聚合里而不是端点里</b>：US-E8-05 的"5 次失败锁 15 分钟"是账号级的业务规则，
/// 放在端点里意味着"任何新的登录入口都要记得抄一遍"，而漏抄的那一次就是爆破入口。
/// </para>
/// </remarks>
public sealed class AdminUser
{
    /// <summary>连续失败多少次锁定（US-E8-05）。</summary>
    public const int MaxFailedLoginAttempts = 5;

    /// <summary>锁定时长（US-E8-05）。</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private AdminUser(
        Guid id,
        string email,
        string emailNormalized,
        string passwordHash,
        string displayName,
        AdminRole role,
        AdminUserStatus status,
        int failedLoginCount,
        DateTimeOffset? lockoutUntil,
        DateTimeOffset? lastLoginAt,
        DateTimeOffset? passwordChangedAt,
        long version)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("ADMIN_ID_REQUIRED", "AdminUser id must not be empty.");
        }

        Id = id;
        Email = GuardEmail(email);
        EmailNormalized = GuardNormalized(emailNormalized);
        PasswordHash = GuardPasswordHash(passwordHash);
        DisplayName = GuardDisplayName(displayName);
        Role = role;
        Status = status;
        FailedLoginCount = failedLoginCount < 0 ? 0 : failedLoginCount;
        LockoutUntil = lockoutUntil;
        LastLoginAt = lastLoginAt;
        PasswordChangedAt = passwordChangedAt;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>原样保留大小写用于展示。</summary>
    public string Email { get; }

    /// <summary>用于唯一索引与查找的规范化邮箱（<c>Trim().ToLowerInvariant()</c>）。</summary>
    public string EmailNormalized { get; }

    public string PasswordHash { get; private set; }

    public string DisplayName { get; private set; }

    public AdminRole Role { get; private set; }

    public AdminUserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutUntil { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    /// <summary>乐观锁版本号；改密/改角色时递增，也让"改密即踢下线"有据可依。</summary>
    public long Version { get; private set; }

    /// <summary>账号当前是否处于锁定期。</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && now < until;

    public static AdminUser Create(
        Guid id,
        string email,
        string passwordHash,
        string displayName,
        AdminRole role,
        DateTimeOffset now)
    {
        var user = new AdminUser(
            id,
            email,
            NormalizeEmail(email),
            passwordHash,
            displayName,
            role,
            AdminUserStatus.Active,
            failedLoginCount: 0,
            lockoutUntil: null,
            lastLoginAt: null,
            passwordChangedAt: null,
            version: 0);

        // 建号即视为"密码已经设过一次"：否则安全策略里"密码多久没换"会把新账号算成过期
        user.PasswordChangedAt = now;
        return user;
    }

    public static AdminUser Rehydrate(
        Guid id,
        string email,
        string emailNormalized,
        string passwordHash,
        string displayName,
        AdminRole role,
        AdminUserStatus status,
        int failedLoginCount,
        DateTimeOffset? lockoutUntil,
        DateTimeOffset? lastLoginAt,
        DateTimeOffset? passwordChangedAt,
        long version)
        => new(id, email, emailNormalized, passwordHash, displayName, role, status, failedLoginCount,
            lockoutUntil, lastLoginAt, passwordChangedAt, version);

    /// <summary>
    /// 登录前置断言。<b>密码正确性不在这里判</b>——本方法只回答"这个账号此刻是否允许尝试登录"。
    /// </summary>
    /// <remarks>
    /// 顺序很重要：先判停用再判锁定。停用是终态（管理员不能自己解锁），锁定是暂时态；
    /// 反过来判会让被停用账号收到"15 分钟后可再试"的误导提示，等于告诉攻击者"继续试"。
    /// </remarks>
    public void EnsureCanAttemptLogin(DateTimeOffset now)
    {
        if (Status == AdminUserStatus.Suspended)
        {
            throw new DomainException("AUTH_ACCOUNT_SUSPENDED", $"Account '{Email}' is suspended.");
        }

        if (IsLockedOut(now))
        {
            throw new DomainException(
                "AUTH_LOCKED",
                $"Account '{Email}' is locked until {LockoutUntil:O} after {MaxFailedLoginAttempts} failed attempts.");
        }
    }

    /// <summary>
    /// 记录一次登录失败。达到阈值即锁定；<b>锁定期内不再累加</b>（否则解锁后一次失手立刻又锁）。
    /// </summary>
    /// <returns>本次失败后是否进入锁定。</returns>
    public bool RegisterFailedLogin(DateTimeOffset now)
    {
        if (IsLockedOut(now))
        {
            return true;
        }

        FailedLoginCount++;
        Version++;

        if (FailedLoginCount >= MaxFailedLoginAttempts)
        {
            LockoutUntil = now + LockoutDuration;
            return true;
        }

        return false;
    }

    /// <summary>记录一次成功登录：清空失败计数与锁定时间。</summary>
    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
        Version++;
    }

    /// <summary>改密。调用方负责随后吊销该用户全部会话（INV-AD02）。</summary>
    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = GuardPasswordHash(newPasswordHash);
        PasswordChangedAt = now;
        Version++;
    }

    /// <summary>改角色。最后一名 Admin 不可降级（INV-AD03）由应用层用"剩余 Admin 计数"保证。</summary>
    public void ChangeRole(AdminRole role)
    {
        if (Role == role)
        {
            return;
        }

        Role = role;
        Version++;
    }

    /// <summary>停用账号（US-E8-01：停用后该用户全部会话失效）。</summary>
    public void Suspend()
    {
        if (Status == AdminUserStatus.Suspended)
        {
            return;
        }

        Status = AdminUserStatus.Suspended;
        Version++;
    }

    public void Activate()
    {
        if (Status == AdminUserStatus.Active)
        {
            return;
        }

        Status = AdminUserStatus.Active;
        FailedLoginCount = 0;
        LockoutUntil = null;
        Version++;
    }

    /// <summary>规范化邮箱：唯一索引与查找都基于它，避免 <c>A@x.com</c> 与 <c>a@x.com</c> 建成两个账号。</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string GuardEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvariantViolationException("ADMIN_EMAIL_REQUIRED", "Admin email is required.");
        }

        var trimmed = email.Trim();
        if (trimmed.Length > 320)
        {
            throw new InvariantViolationException("ADMIN_EMAIL_TOO_LONG", "Admin email must be <= 320 chars.");
        }

        // 只做"有一个 @ 且两侧非空"的最低校验：RFC 5322 全量正则既长又会误杀合法地址，
        // 真正的验证手段是登录邮件/重置邮件能到达（MVP 不做邮件验证，故不做过强校验）
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == trimmed.Length - 1)
        {
            throw new InvariantViolationException("ADMIN_EMAIL_INVALID", $"'{email}' is not a valid email address.");
        }

        return trimmed;
    }

    private static string GuardNormalized(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvariantViolationException("ADMIN_EMAIL_NORMALIZED_REQUIRED", "Normalized email is required.");
        }

        return normalized;
    }

    private static string GuardPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            // 空哈希 = 空密码可登录，这是最危险的一类脏数据，宁可在写入侧炸
            throw new InvariantViolationException("ADMIN_PASSWORD_HASH_REQUIRED", "Password hash must not be empty.");
        }

        return passwordHash;
    }

    private static string GuardDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvariantViolationException("ADMIN_DISPLAY_NAME_REQUIRED", "Admin display name is required.");
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length > 100)
        {
            throw new InvariantViolationException("ADMIN_DISPLAY_NAME_TOO_LONG", "Display name must be <= 100 chars.");
        }

        return trimmed;
    }
}
