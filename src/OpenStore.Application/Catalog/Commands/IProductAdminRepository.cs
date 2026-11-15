using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Commands;

/// <summary>
/// 后台商品写入端口。<b>与 <see cref="Queries.IProductRepository"/> 分开</b>：
/// 读路径可以走定制投影，写路径必须拿到聚合根；混在一个接口里最后一定变成 "IFooRepository 有 40 个方法"。
/// </summary>
public interface IProductAdminRepository
{
    /// <summary>按 Id 取聚合（含变体）。不存在返回 <c>null</c>。</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>新增聚合（含全部变体）。Slug/SKU 冲突由持久化层唯一索引兜底。</summary>
    Task AddAsync(Product product, CancellationToken ct);

    /// <summary>落盘聚合：同步标量字段（Slug/Title/Status）与既有变体的 SKU/价格。M0 不支持变体增删。</summary>
    Task SaveAsync(Product product, CancellationToken ct);

    /// <summary>Slug 是否已被占用（用于创建前的冲突预检，给出可读错误码而不是 500）。</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);
}
