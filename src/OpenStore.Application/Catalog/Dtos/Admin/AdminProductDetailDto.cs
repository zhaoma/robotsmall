using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台商品详情（含变体）。<b>属于 admin 契约，绝不进入 storefront 命名空间</b>。
/// </summary>
public sealed record AdminProductDetailDto(
    Guid Id,
    string Slug,
    string Title,
    ProductStatus Status,
    IReadOnlyList<AdminProductVariantDto> Variants);
