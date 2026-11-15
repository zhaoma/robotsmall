using OpenStore.Domain.Customers;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>Session 持久化模型 ↔ 领域实体的映射（纯函数，无 IO）。</summary>
public static class SessionMapping
{
    public static Session ToDomain(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Session.Rehydrate(
            record.TokenHash,
            (SessionActorType)record.ActorType,
            record.ActorId,
            record.CreatedAt,
            record.LastSeenAt,
            record.IdleExpiresAt,
            record.AbsoluteExpiresAt,
            record.IpHash,
            record.UserAgentHash,
            record.CsrfHash,
            record.RevokedAt,
            record.RevokeReason);
    }

    public static SessionRecord ToRecord(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionRecord
        {
            TokenHash = session.TokenHash,
            ActorType = (int)session.ActorType,
            ActorId = session.ActorId,
            CreatedAt = session.CreatedAt,
            LastSeenAt = session.LastSeenAt,
            IdleExpiresAt = session.IdleExpiresAt,
            AbsoluteExpiresAt = session.AbsoluteExpiresAt,
            IpHash = session.IpHash,
            UserAgentHash = session.UserAgentHash,
            CsrfHash = session.CsrfHash,
            RevokedAt = session.RevokedAt,
            RevokeReason = session.RevokeReason,
        };
    }

    /// <summary>把实体的可变状态（滑动续期 / 撤销）写回既有记录。</summary>
    public static void Apply(Session session, SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(record);

        record.LastSeenAt = session.LastSeenAt;
        record.IdleExpiresAt = session.IdleExpiresAt;
        record.RevokedAt = session.RevokedAt;
        record.RevokeReason = session.RevokeReason;
    }
}
