using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Abstractions;
using OpenStore.Domain.Customers;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary><see cref="IAdminUserRepository"/> 的 SQLite 实现。</summary>
public sealed class SqliteAdminUserRepository(StoreDbContext db) : IAdminUserRepository
{
    public async Task<AdminUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var record = await db.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.EmailNormalized == normalizedEmail, ct)
            .ConfigureAwait(false);

        return record is null ? null : AdminUserMapping.ToDomain(record);
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var record = await db.AdminUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false);

        return record is null ? null : AdminUserMapping.ToDomain(record);
    }

    /// <summary>新增或更新。<c>Version</c> 乐观锁的并发冲突检测随 M1 的 SaveChanges 拦截器一起引入。</summary>
    public async Task SaveAsync(AdminUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var record = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            db.AdminUsers.Add(AdminUserMapping.ToRecord(user));
        }
        else
        {
            AdminUserMapping.Apply(user, record);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken ct)
        => await db.AdminUsers
            .AsNoTracking()
            .CountAsync(u => u.Status == (int)AdminUserStatus.Active, ct)
            .ConfigureAwait(false);

    public async Task<PagedResult<AdminUser>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var total = await db.AdminUsers.AsNoTracking().CountAsync(ct).ConfigureAwait(false);

        var records = await db.AdminUsers
            .AsNoTracking()
            .OrderBy(u => u.EmailNormalized)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = records.Select(AdminUserMapping.ToDomain).ToList();
        return new PagedResult<AdminUser>(items, page.Page, page.PageSize, total);
    }

    public Task<bool> AnyAsync(CancellationToken ct)
        => db.AdminUsers.AsNoTracking().AnyAsync(ct);
}
