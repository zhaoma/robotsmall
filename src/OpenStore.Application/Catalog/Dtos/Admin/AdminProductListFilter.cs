using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台商品列表查询条件（已由 <c>AdminProductQueryService</c> 校验/归一化后传给读端口）。
/// </summary>
public sealed record AdminProductListFilter(
    string? Query,
    ProductStatus? Status,
    string? Sort,
    int Page,
    int PageSize);
