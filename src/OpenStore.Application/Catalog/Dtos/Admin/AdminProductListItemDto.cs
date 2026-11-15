using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台商品列表项。<b>属于 admin 契约，绝不进入 storefront 命名空间</b>。
/// </summary>
/// <remarks>
/// M0 只暴露当前物理模型真实存在的字段（Id/Slug/Title/Status/VariantCount）。
/// 库存（totalOnHand）与成本在 M1 接入 Inventory 上下文后再加，避免在 M0 编造列。
/// </remarks>
public sealed record AdminProductListItemDto(
    Guid Id,
    string Slug,
    string Title,
    ProductStatus Status,
    int VariantCount);
