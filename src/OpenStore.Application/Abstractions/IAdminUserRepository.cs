using OpenStore.Domain.Customers;

namespace OpenStore.Application.Abstractions;

/// <summary>
/// 管理员账号仓储端口。
/// </summary>
/// <remarks>
/// <b>只暴露"按规范化邮箱查"这一个查找入口</b>：登录时若按原样邮箱查，<c>A@x.com</c> 与
/// <c>a@x.com</c> 会变成两个账号（唯一索引建在 <c>EmailNormalized</c> 上，DB 不会拦），
/// 而用户会认为"我明明注册过"。规范化必须在调用方之前完成一次、只完成一次，
/// 所以端口签名直接叫 <c>normalizedEmail</c>，逼调用方想清楚传的是什么。
/// </remarks>
public interface IAdminUserRepository
{
    /// <summary>按规范化邮箱查。不存在返回 <c>null</c>（<b>不抛异常</b>：登录流程要靠 null 走"统一错误码"分支）。</summary>
    Task<AdminUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>保存（新增或更新）。更新走 <c>Version</c> 乐观锁。</summary>
    Task SaveAsync(AdminUser user, CancellationToken ct);

    /// <summary>
    /// 统计处于 Active 的管理员数量。用于 INV-AD03「最后一名 Admin 不可降级/停用」。
    /// </summary>
    /// <remarks>
    /// 调用方必须与"降级操作"在<b>同一事务</b>里调用它，否则两个并发请求会各自看到 2 个 Admin
    /// 然后都降级成功，最后系统没有管理员。
    /// </remarks>
    Task<int> CountActiveAdminsAsync(CancellationToken ct);

    /// <summary>后台管理员列表。</summary>
    Task<PagedResult<AdminUser>> ListAsync(PageRequest page, CancellationToken ct);

    /// <summary>是否存在任何管理员（首次安装判定：没有则向导允许创建首个 Admin）。</summary>
    Task<bool> AnyAsync(CancellationToken ct);
}
