using OpenStore.Domain.Customers;

namespace OpenStore.Application.Abstractions;

/// <summary>
/// 当前请求的身份。<b>所有"这是谁"的判断只能从这里取</b>，绝不从请求体/查询串取。
/// </summary>
/// <remarks>
/// <b>越权防护的落点</b>（后端设计 §6.5）：DTO 里根本没有 <c>customerId</c> / <c>adminId</c> 字段，
/// 服务层写 <c>CurrentUser.UserId</c>。这样"改个参数就能看别人的数据"在类型层面就不成立，
/// 而不是靠每个用例记得加过滤条件。
/// </remarks>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>当前主体主键（管理员或客户）；未登录为 <c>null</c>。</summary>
    Guid? UserId { get; }

    /// <summary>管理员角色；客户与未登录为 <c>null</c>。</summary>
    AdminRole? Role { get; }

    /// <summary>当前主体的权限点集合（仅用于 UX 与二线校验，<b>服务端策略是唯一权威</b>）。</summary>
    IReadOnlySet<string> Permissions { get; }

    string? Email { get; }

    string? DisplayName { get; }

    /// <summary>当前会话的撤销原因用于排障时判断"是不是被踢了"；无会话为 <c>null</c>。</summary>
    bool HasPermission(string permission);
}
