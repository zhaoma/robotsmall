using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers;

/// <summary>管理员账号写入/读取端口。实现放在 Infrastructure/Persistence。</summary>
public interface IAdminUserRepository
{
    /// <summary>按规范化邮箱查聚合。不存在返回 <c>null</c>。</summary>
    Task<AdminUser?> FindByNormalizedEmailAsync(string emailNormalized, CancellationToken ct);

    /// <summary>按 Id 查聚合。不存在返回 <c>null</c>。</summary>
    Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>新增账号（安装向导/种子数据）。</summary>
    Task AddAsync(AdminUser user, CancellationToken ct);

    /// <summary>落盘聚合（登录失败/成功计数、锁定、改密等状态迁移后）。</summary>
    Task UpdateAsync(AdminUser user, CancellationToken ct);

    /// <summary>是否已存在至少一个账号（种子数据判断用）。</summary>
    Task<bool> AnyAsync(CancellationToken ct);
}
