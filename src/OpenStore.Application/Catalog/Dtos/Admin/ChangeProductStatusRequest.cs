using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台：PATCH /api/admin/products/{id}/status 的请求体。
/// </summary>
/// <remarks>
/// 用领域枚举而不是 int/string：契约与领域状态机共用同一份定义，
/// 避免"API 接受 4、领域不认识 4"这类靠字符串魔法值维持的接口（枚举越界在聚合里被拒，返回 400）。
/// </remarks>
public sealed record ChangeProductStatusRequest(ProductStatus Status);
