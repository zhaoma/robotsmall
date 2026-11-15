namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台：PATCH /api/admin/products/{id} 的请求体。所有字段可选，只改显式给出的字段。
/// </summary>
public sealed record UpdateProductRequest(
    string? Title,
    IReadOnlyList<UpdateProductVariantRequest>? Variants);

/// <summary>编辑商品时的变体改动（按变体 Id 定位）。M0 支持改 SKU 与价格。</summary>
public sealed record UpdateProductVariantRequest(
    Guid Id,
    string? Sku,
    AdminMoneyDto? Price);
