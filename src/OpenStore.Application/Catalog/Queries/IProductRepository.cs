using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Queries;

/// <summary>
/// 门店侧商品读取端口（只读）。实现放在 Infrastructure/Persistence。
/// </summary>
/// <remarks>
/// 刻意只有"按 slug 取单个"这一个方法：storefront 的读取必须走"Active + 已翻译"的过滤，
/// 端口越窄，越不可能有人在用例里顺手写出"把 Draft 商品也查出来"的代码。
/// 列表 / facet 查询在 M1 用独立的 <c>IProductListQuery</c>（读模型）实现，不往这里加方法。
/// </remarks>
public interface IProductRepository
{
    /// <summary>按 slug 取商品聚合（含变体）。不存在返回 <c>null</c>。是否存在/上架判断由调用方做。</summary>
    Task<Product?> GetBySlugAsync(string slug, CancellationToken ct);
}
