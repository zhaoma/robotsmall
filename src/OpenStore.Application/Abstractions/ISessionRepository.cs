using OpenStore.Domain.Customers;

namespace OpenStore.Application.Abstractions;

/// <summary>
/// 会话仓储端口。
/// </summary>
/// <remarks>
/// <para>
/// <b>签名里只有 <c>tokenHash</c>，没有 raw token</b>——这是刻意的类型级防线：
/// 一旦某个实现不小心把 raw token 当参数接收并写进日志或 DB，就等于把会话拱手让人。
/// 哈希在认证处理器里算一次，之后全链路只见哈希。
/// </para>
/// <para>
/// <b><see cref="TouchAsync"/> 必须是单条条件 UPDATE</b>（<c>WHERE TokenHash=@h AND LastSeenAt &lt; @threshold</c>），
/// 返回受影响行数是否 &gt; 0。不许实现成"读出来 → 判断 → 写回去"：浏览器同时打 6 个接口是常态，
/// 先读后写会让 5 个请求白写，且在 SQLite 下争抢写锁。
/// </para>
/// </remarks>
public interface ISessionRepository
{
    /// <summary>新建会话（登录成功时）。</summary>
    Task CreateAsync(Session session, CancellationToken ct);

    /// <summary>按 token 哈希取会话；不存在返回 <c>null</c>。</summary>
    Task<Session?> FindAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// 滑动续期（节流）。仅当 <c>LastSeenAt &lt; throttleBefore</c> 时才写，
    /// 返回 <c>true</c> 表示本次真的写成功了。
    /// </summary>
    Task<bool> TouchAsync(string tokenHash, DateTimeOffset now, DateTimeOffset newIdleExpiresAt, DateTimeOffset throttleBefore, CancellationToken ct);

    /// <summary>撤销单个会话（登出）。</summary>
    Task RevokeAsync(string tokenHash, DateTimeOffset now, string reason, CancellationToken ct);

    /// <summary>
    /// 撤销某主体的全部会话（改密、停用、角色变更）。
    /// </summary>
    /// <remarks>
    /// <b>这是"改密即踢下线"的全部实现</b>：一条 UPDATE。正因为它是"一条 UPDATE 而不是 N 次写"，
    /// 才不需要 <c>SecurityStamp</c> 那一列（见 <c>Session</c> 类注释里的裁决）。
    /// </remarks>
    Task RevokeAllForActorAsync(SessionActorType actorType, Guid actorId, DateTimeOffset now, string reason, CancellationToken ct);

    /// <summary>轮换 CSRF 秘钥哈希（登录成功 / 改密后，防 fixation）。</summary>
    Task RotateCsrfAsync(string tokenHash, string csrfHash, CancellationToken ct);

    /// <summary>
    /// 清理过期与陈旧的撤销记录。返回删除行数。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="keepRevokedFor">已撤销记录保留多久用于排障（默认 30 天）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, TimeSpan keepRevokedFor, CancellationToken ct);
}
