using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台：商品状态变更结果。属于 admin 契约，<b>绝不能</b>被 storefront 端点返回。
/// </summary>
public sealed record AdminProductStatusDto(Guid Id, string Slug, ProductStatus Status);
