using OpenStore.Application.Catalog.Dtos.Admin;

namespace OpenStore.Application.Catalog.Queries;

/// <summary>
/// 后台商品列表读端口（只读，读模型）。与写端口 <c>Commands.IProductAdminRepository</c> 分离：
/// 列表走投影，写路径必须拿聚合根（后端设计 §1.1 "列表走读模型"的约定）。
/// </summary>
public interface IAdminProductQuery
{
    /// <summary>分页查询商品列表。入参由用例层校验/归一化，本端口不再重复校验。</summary>
    Task<AdminProductListResponse> ListAsync(AdminProductListFilter filter, CancellationToken ct);
}
