using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Domain.Catalog;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IAdminProductQuery"/> 的 SQLite 实现（只读投影，不物化领域聚合）。
/// </summary>
public sealed class SqliteAdminProductQuery(StoreDbContext db) : IAdminProductQuery
{
    public async Task<AdminProductListResponse> ListAsync(AdminProductListFilter filter, CancellationToken ct)
    {
        IQueryable<Records.ProductRecord> query = db.Products.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(p => p.Status == (int)status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var keyword = filter.Query.Trim();
            query = query.Where(p => p.Title.Contains(keyword) || p.Slug.Contains(keyword));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        query = filter.Sort switch
        {
            "title_asc" => query.OrderBy(p => p.Title).ThenBy(p => p.Slug),
            "title_desc" => query.OrderByDescending(p => p.Title).ThenBy(p => p.Slug),
            "slug_desc" => query.OrderByDescending(p => p.Slug),
            "status_asc" => query.OrderBy(p => p.Status).ThenBy(p => p.Slug),
            "status_desc" => query.OrderByDescending(p => p.Status).ThenBy(p => p.Slug),
            _ => query.OrderBy(p => p.Slug), // slug_asc 默认
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new AdminProductListItemDto(
                p.Id,
                p.Slug,
                p.Title,
                (ProductStatus)p.Status,
                p.Variants.Count))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pageCount = total == 0 ? 0 : (int)Math.Ceiling(total / (double)filter.PageSize);

        return new AdminProductListResponse(items, filter.Page, filter.PageSize, total, pageCount);
    }
}
