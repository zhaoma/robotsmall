using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Customers;
using OpenStore.Domain.Customers;
using OpenStore.Domain.Shared;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary><see cref="IAdminUserRepository"/> 的 SQLite 实现。</summary>
public sealed class SqliteAdminUserRepository(StoreDbContext db) : IAdminUserRepository
{
    public async Task<AdminUser?> FindByNormalizedEmailAsync(string emailNormalized, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized))
        {
            return null;
        }

        var record = await db.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, ct)
            .ConfigureAwait(false);

        return record is null ? null : AdminUserMapping.ToDomain(record);
    }

    public async Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var record = await db.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false);

        return record is null ? null : AdminUserMapping.ToDomain(record);
    }

    public async Task AddAsync(AdminUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        db.AdminUsers.Add(AdminUserMapping.ToRecord(user));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AdminUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var record = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct)
            .ConfigureAwait(false)
            ?? throw new DomainException(
                "ADMIN_NOT_FOUND",
                $"AdminUser '{user.Id}' disappeared between read and write.");

        AdminUserMapping.Apply(user, record);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<bool> AnyAsync(CancellationToken ct)
        => db.AdminUsers.AsNoTracking().AnyAsync(ct);
}
