using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Customers;
using OpenStore.Domain.Customers;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary><see cref="IAdminSessionRepository"/> 的 SQLite 实现。</summary>
public sealed class SqliteAdminSessionRepository(StoreDbContext db) : IAdminSessionRepository
{
    public async Task<AuthenticatedAdmin?> AuthenticateAsync(string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return null;
        }

        var record = await db.Sessions
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        var session = SessionMapping.ToDomain(record);
        if (!session.IsUsable(now) || session.ActorType != SessionActorType.Admin)
        {
            return null;
        }

        // 主体状态不冗余在 Session 表里，认证时另查（Session.cs 注释：避免旧权限/旧状态残留）
        var adminRecord = await db.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == session.ActorId, ct)
            .ConfigureAwait(false);

        if (adminRecord is null)
        {
            return null;
        }

        var admin = AdminUserMapping.ToDomain(adminRecord);
        if (admin.Status != AdminUserStatus.Active)
        {
            return null;
        }

        // 滑动续期（节流写）：影响 0 行 = "别人刚写过"，跳过即可
        if (session.TryTouch(now, AdminSessionPolicy.IdleTimeout))
        {
            SessionMapping.Apply(session, record);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new AuthenticatedAdmin(admin.Id, admin.Email, admin.DisplayName, admin.Role);
    }

    public async Task AddAsync(Session session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        db.Sessions.Add(SessionMapping.ToRecord(session));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return;
        }

        var record = await db.Sessions
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            return; // 幂等：登出时会话可能已经不存在/已撤销
        }

        var session = SessionMapping.ToDomain(record);
        session.Revoke(now, SessionRevokeReason.Logout);
        SessionMapping.Apply(session, record);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
