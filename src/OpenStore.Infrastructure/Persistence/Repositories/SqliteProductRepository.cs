using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Domain.Catalog;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IProductRepository"/> 的 SQLite 实现（薄：只做查询，不含业务判断）。
/// </summary>
/// <remarks>
/// <c>AsNoTracking</c> 是刻意的：这是读路径，且返回的领域对象是<b>重新构造</b>出来的，
/// 与 DbContext 的变更跟踪无关，跟踪只会白白占内存并让后续 SaveChanges 产生意外 UPDATE。
/// </remarks>
public sealed class SqliteProductRepository(StoreDbContext db) : IProductRepository
{
    public async Task<Product?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var record = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct)
            .ConfigureAwait(false);

        return record is null ? null : ProductMapping.ToDomain(record);
    }
}
