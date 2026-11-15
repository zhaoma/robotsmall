namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台商品详情里的单个变体。
/// </summary>
/// <remarks>
/// 价格用单对象 <c>Price</c>（后端设计 §5.4：变体价格 = <c>{amountMinor,currency}</c> 单对象，
/// 不因为 M0 内联列就变成数组）。M0 单币种，多币种写会被持久化层拒绝。
/// </remarks>
public sealed record AdminProductVariantDto(
    Guid Id,
    string Sku,
    string AxisSignature,
    AdminMoneyDto? Price);
