namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台：POST /api/admin/products 的请求体。
/// </summary>
public sealed record CreateProductRequest(
    string Slug,
    string Title,
    IReadOnlyList<CreateProductVariantRequest> Variants);

/// <summary>新建商品时的变体。轴值由 <see cref="Domain.Catalog.VariantMatrix.BuildSignature"/> 归一化为签名；
/// 单规格商品（无轴）传 <c>null</c> 或空字典，签名落空串。</summary>
public sealed record CreateProductVariantRequest(
    string Sku,
    IReadOnlyDictionary<string, string>? Axes,
    AdminMoneyDto Price);
