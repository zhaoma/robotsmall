namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台商品列表分页响应。字段名遵循后端设计 §5.1 的统一分页形状：
/// <c>items / page / pageSize / total / pageCount</c>（<c>pageCount</c> 不是 <c>totalPages</c>）。
/// </summary>
public sealed record AdminProductListResponse(
    IReadOnlyList<AdminProductListItemDto> Items,
    int Page,
    int PageSize,
    int Total,
    int PageCount);
