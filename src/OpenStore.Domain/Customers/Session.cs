using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Customers;

/// <summary>会话主体类型。</summary>
public enum SessionActorType
{
    Admin = 0,
    Customer = 1,
}

/// <summary>撤销原因的稳定取值（写进 DB 的 <c>RevokeReason</c> 列，不要用自由文本）。</summary>
public static class SessionRevokeReason
{
    public const string Logout = "logout";
    public const string PasswordChanged = "password_changed";
    public const string AdminSuspended = "admin_suspended";
    public const string Rotated = "rotated";
    public const string ReuseDetected = "reuse_detected";
}

/// <summary>
/// 服务端会话。<b>Cookie 里只有随机 token，DB 里只有它的 SHA-256</b>（数据模型 §3.6）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 JWT</b>（架构 §3 已裁决，这里记落地理由）："管理员封禁/改密立即生效"是
/// AC-15 与 US-E8-01 的隐含要求。JWT 要满足它就得加黑名单——那等于又做一个 Session 表，
/// 却多了一套签名/验签/时钟偏移的坑。Session 表 <c>RevokedAt</c> 一行就够。
/// </para>
/// <para>
/// <b>刻意没有并发令牌</b>（后端设计 §6.2）：<c>LastSeenAt</c> 是"节流写"，
/// 实现为单行单调 UPDATE（<c>WHERE LastSeenAt &lt; now-5min</c>），影响 0 行表示
/// "别人刚写过"，这是正常并发而非冲突。给它加乐观锁会让同一用户的并行请求互相 409
/// ——浏览器同时打 6 个接口是常态。能靠幂等写解决的地方不要用乐观锁。
/// </para>
/// <para>
/// <b>关于 <c>SecurityStamp</c></b>：后端设计 §6.2 验证顺序第 ⑤ 步曾提到它，但数据模型定稿的
/// <c>Session</c> 表<b>没有这一列</b>。主理人裁决：<b>以数据模型为准，不引入 SecurityStamp</b>。
/// "改密/停用即踢下线"通过"撤销该主体全部会话"实现（一次 <c>UPDATE ... WHERE ActorId=@id</c>），
/// 语义等价且少一列比对。此处留档，防止后续有人照着 §6.2 去加一个没人维护的列。
/// </para>
/// </remarks>
public sealed class Session
{
    /// <summary>滑动续期的写入节流阈值：5 分钟内不重复写库。</summary>
    public static readonly TimeSpan TouchThrottle = TimeSpan.FromMinutes(5);

    private Session(
        string tokenHash,
        SessionActorType actorType,
        Guid actorId,
        DateTimeOffset createdAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt,
        string? ipHash,
        string? userAgentHash,
        string? csrfHash,
        DateTimeOffset? revokedAt,
        string? revokeReason)
    {
        TokenHash = GuardHash(tokenHash, nameof(tokenHash));
        ActorType = actorType;
        ActorId = actorId == Guid.Empty
            ? throw new InvariantViolationException("SESSION_ACTOR_REQUIRED", "Session actorId must not be empty.")
            : actorId;
        CreatedAt = createdAt;
        LastSeenAt = lastSeenAt;
        IdleExpiresAt = idleExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;

        if (absoluteExpiresAt <= createdAt)
        {
            // 绝对过期早于创建时间 = 建出来就已经过期，属于调用方算错了时长
            throw new InvariantViolationException(
                "SESSION_ABSOLUTE_EXPIRY_INVALID",
                $"AbsoluteExpiresAt ({absoluteExpiresAt:O}) must be after CreatedAt ({createdAt:O}).");
        }

        IpHash = ipHash;
        UserAgentHash = userAgentHash;
        CsrfHash = csrfHash;
        RevokedAt = revokedAt;
        RevokeReason = revokeReason;
    }

    /// <summary><c>SHA-256(rawToken)</c> 的小写 hex，主键。DB 泄露不等于会话可被劫持。</summary>
    public string TokenHash { get; }

    public SessionActorType ActorType { get; }

    public Guid ActorId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>滑动过期：<c>LastSeenAt + IdleMinutes</c>。</summary>
    public DateTimeOffset IdleExpiresAt { get; private set; }

    /// <summary>绝对过期：创建时定死，防"永久在线"。</summary>
    public DateTimeOffset AbsoluteExpiresAt { get; }

    /// <summary>IP 的 hash（不存原 IP，减少 PII 面）。仅审计，<b>不做会话绑定</b>。</summary>
    public string? IpHash { get; }

    public string? UserAgentHash { get; }

    /// <summary>服务端持有的 CSRF 秘钥 hash，供 double-submit 校验（仅 Admin 有）。</summary>
    public string? CsrfHash { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static Session Create(
        string tokenHash,
        SessionActorType actorType,
        Guid actorId,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        TimeSpan absoluteTimeout,
        string? ipHash = null,
        string? userAgentHash = null,
        string? csrfHash = null)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new InvariantViolationException("SESSION_IDLE_TIMEOUT_INVALID", "Idle timeout must be positive.");
        }

        if (absoluteTimeout <= TimeSpan.Zero)
        {
            throw new InvariantViolationException("SESSION_ABSOLUTE_TIMEOUT_INVALID", "Absolute timeout must be positive.");
        }

        return new Session(
            tokenHash,
            actorType,
            actorId,
            createdAt: now,
            lastSeenAt: now,
            idleExpiresAt: now + idleTimeout,
            absoluteExpiresAt: now + absoluteTimeout,
            ipHash,
            userAgentHash,
            csrfHash,
            revokedAt: null,
            revokeReason: null);
    }

    public static Session Rehydrate(
        string tokenHash,
        SessionActorType actorType,
        Guid actorId,
        DateTimeOffset createdAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt,
        string? ipHash,
        string? userAgentHash,
        string? csrfHash,
        DateTimeOffset? revokedAt,
        string? revokeReason)
        => new(tokenHash, actorType, actorId, createdAt, lastSeenAt, idleExpiresAt, absoluteExpiresAt,
            ipHash, userAgentHash, csrfHash, revokedAt, revokeReason);

    /// <summary>
    /// 会话此刻是否可用。<b>不查账号状态</b>——账号是否停用由认证处理器在同一事务里另查
    /// （见 <c>SessionAuthenticationHandler</c>），因为 Session 表刻意不冗余角色/状态，
    /// 否则"改了角色但会话里还是旧权限"就成了越权窗口。
    /// </summary>
    public bool IsUsable(DateTimeOffset now)
        => !IsRevoked && now < AbsoluteExpiresAt && now < IdleExpiresAt;

    /// <summary>
    /// 滑动续期。返回 <c>true</c> 表示调用方应当写库（已过节流阈值），<c>false</c> 表示"刚有人写过，跳过"。
    /// </summary>
    /// <remarks>
    /// 实现约束：调用方必须把它落成单条条件 UPDATE
    /// （<c>WHERE TokenHash=@h AND LastSeenAt &lt; @threshold</c>），<b>不要先读后写</b>——
    /// 先读后写会让并发的 6 个请求里 5 个白写，且在 SQLite 下争写锁。
    /// </remarks>
    public bool TryTouch(DateTimeOffset now, TimeSpan idleTimeout)
    {
        if (!IsUsable(now))
        {
            return false;
        }

        if (now - LastSeenAt < TouchThrottle)
        {
            return false;
        }

        LastSeenAt = now;
        IdleExpiresAt = now + idleTimeout;
        return true;
    }

    /// <summary>撤销会话（登出、改密、停用、主动轮换）。</summary>
    public void Revoke(DateTimeOffset now, string reason)
    {
        if (IsRevoked)
        {
            return; // 幂等：重复登出不报错
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvariantViolationException("SESSION_REVOKE_REASON_REQUIRED", "Revoke reason is required.");
        }

        RevokedAt = now;
        RevokeReason = reason;
    }

    /// <summary>登录成功或改密后轮换 CSRF 秘钥（防 fixation）。</summary>
    public void RotateCsrf(string csrfHash)
    {
        CsrfHash = GuardHash(csrfHash, nameof(csrfHash));
    }

    private static string GuardHash(string hash, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new InvariantViolationException("SESSION_HASH_REQUIRED", $"{parameterName} must not be empty.");
        }

        if (hash.Length != 64)
        {
            // 统一存 SHA-256 的 hex（64 字符）。长度不对说明有人传了 raw token，这是最危险的失误：
            // 一旦 raw token 落库，DB 泄露就等于会话直接被劫持
            throw new InvariantViolationException(
                "SESSION_HASH_INVALID",
                $"{parameterName} must be a 64-char SHA-256 hex string, got length {hash.Length}.");
        }

        return hash;
    }
}
