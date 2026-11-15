using OpenStore.Domain.Customers;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>AdminUser 持久化模型 ↔ 领域聚合的映射（纯函数，无 IO）。</summary>
public static class AdminUserMapping
{
    public static AdminUser ToDomain(AdminUserRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return AdminUser.Rehydrate(
            record.Id,
            record.Email,
            record.EmailNormalized,
            record.PasswordHash,
            record.DisplayName,
            (AdminRole)record.Role,
            (AdminUserStatus)record.Status,
            record.FailedLoginCount,
            record.LockoutUntil,
            record.LastLoginAt,
            record.PasswordChangedAt,
            record.Version);
    }

    public static AdminUserRecord ToRecord(AdminUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var record = new AdminUserRecord();
        Apply(user, record);
        return record;
    }

    /// <summary>把聚合的可变状态写回既有记录（Id 不变）。</summary>
    public static void Apply(AdminUser user, AdminUserRecord record)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(record);

        record.Id = user.Id;
        record.Email = user.Email;
        record.EmailNormalized = user.EmailNormalized;
        record.PasswordHash = user.PasswordHash;
        record.DisplayName = user.DisplayName;
        record.Role = (int)user.Role;
        record.Status = (int)user.Status;
        record.FailedLoginCount = user.FailedLoginCount;
        record.LockoutUntil = user.LockoutUntil;
        record.LastLoginAt = user.LastLoginAt;
        record.PasswordChangedAt = user.PasswordChangedAt;
        record.Version = user.Version;
    }
}
