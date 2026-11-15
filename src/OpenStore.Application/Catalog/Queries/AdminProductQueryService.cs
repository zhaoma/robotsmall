using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Domain.Shared;

namespace OpenStore.Application.Catalog.Queries;

/// <summary>
/// 后台商品列表用例。负责把 HTTP 查询参数校验/归一化成读端口能直接执行的过滤条件。
/// </summary>
public sealed class AdminProductQueryService(IAdminProductQuery products)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxPage = 100;

    private static readonly HashSet<string> AllowedSorts = new(StringComparer.Ordinal)
    {
        "slug_asc", "slug_desc", "title_asc", "title_desc", "status_asc", "status_desc",
    };

    public Task<AdminProductListResponse> ListAsync(AdminProductListFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = filter.Page < 1 ? 1 : filter.Page;
        if (page > MaxPage)
        {
            throw new DomainException("PAGE_OUT_OF_RANGE", $"page must be <= {MaxPage}, got {filter.Page}.");
        }

        var pageSize = filter.PageSize < 1 ? DefaultPageSize : filter.PageSize;
        if (pageSize > MaxPageSize)
        {
            throw new DomainException("PAGE_SIZE_EXCEEDED", $"pageSize must be <= {MaxPageSize}, got {filter.PageSize}.");
        }

        var sort = string.IsNullOrWhiteSpace(filter.Sort) ? "slug_asc" : filter.Sort.Trim().ToLowerInvariant();
        if (!AllowedSorts.Contains(sort))
        {
            throw new DomainException("VALIDATION_FAILED", $"sort '{filter.Sort}' is not supported.");
        }

        var normalized = filter with { Sort = sort, Page = page, PageSize = pageSize };
        return products.ListAsync(normalized, ct);
    }
}
