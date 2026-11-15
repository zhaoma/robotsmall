using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Abstractions;
using OpenStore.Domain.Customers;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary><see cref="ISessionRepository"/> 的 SQLite 实现。</summary>
/// <remarks>
/// 滑动续期 / 撤销 / 轮换 / 清理全部走单条条件 UPDATE/DELETE（<c>ExecuteUpdateAsync</c> /
/// <c>ExecuteDeleteAsync</c>），不先读后写——避免并发请求在 SQLite 下争写锁（后端设计 §6.2）。
/// </remarks>
public sealed class SqliteSessionRepository(StoreDbContext db) : ISessionRepository
{
    public async Task CreateAsync(Session session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        db.Sessions.Add(SessionMapping.ToRecord(session));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Session?> FindAsync(string tokenHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return null;
        }

        var record = await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

        return record is null ? null : SessionMapping.ToDomain(record);
    }

    public async Task<bool> TouchAsync(
        string tokenHash,
        DateTimeOffset now,
        DateTimeOffset newIdleExpiresAt,
        DateTimeOffset throttleBefore,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return false;
        }

        var affected = await db.Sessions
            .Where(s => s.TokenHash == tokenHash && s.LastSeenAt < throttleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastSeenAt, now)
                .SetProperty(s => s.IdleExpiresAt, newIdleExpiresAt), ct)
            .ConfigureAwait(false);

        return affected > 0;
    }

    public async Task RevokeAsync(string tokenHash, DateTimeOffset now, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return;
        }

        await db.Sessions
            .Where(s => s.TokenHash == tokenHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokeReason, reason), ct)
            .ConfigureAwait(false);
    }

    public async Task RevokeAllForActorAsync(
        SessionActorType actorType,
        Guid actorId,
        DateTimeOffset now,
        string reason,
        CancellationToken ct)
    {
        await db.Sessions
            .Where(s => s.ActorType == (int)actorType && s.ActorId == actorId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokeReason, reason), ct)
            .ConfigureAwait(false);
    }

    public async Task RotateCsrfAsync(string tokenHash, string csrfHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return;
        }

        await db.Sessions
            .Where(s => s.TokenHash == tokenHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.CsrfHash, csrfHash), ct)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, TimeSpan keepRevokedFor, CancellationToken ct)
    {
        var cutoff = now - keepRevokedFor;

        return await db.Sessions
            .Where(s => s.AbsoluteExpiresAt < cutoff
                || (s.RevokedAt != null && s.RevokedAt < cutoff))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
