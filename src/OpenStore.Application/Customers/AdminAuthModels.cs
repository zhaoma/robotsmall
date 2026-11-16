using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers;

/// <summary>登录用例的结果（联合）。失败不抛异常，由端点映射为 401 / 423。</summary>
public abstract record AdminLoginResult;

public sealed record AdminLoginSuccess(
    Guid Id,
    string Email,
    string DisplayName,
    AdminRole Role,
    IReadOnlyList<string> Permissions,
    string SessionToken,
    string CsrfToken) : AdminLoginResult;

public enum AdminLoginFailureReason
{
    /// <summary>账号不存在 / 已停用 / 密码错，统一不区分（防账号枚举）。</summary>
    InvalidCredentials = 0,

    /// <summary>账号处于锁定期。</summary>
    Locked = 1,
}

public sealed record AdminLoginFailure(AdminLoginFailureReason Reason, DateTimeOffset? UnlockAt) : AdminLoginResult;

/// <summary>改密入参。<b>没有 actorId 字段</b>——主体只从 <c>ICurrentUser</c> 取，否则改别人密码只需换一个请求体。</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>改密用例的结果（联合）。</summary>
public abstract record ChangePasswordResult;

/// <summary>
/// 改密成功。<b>此时该管理员全部会话（含发起本次请求的这条）已被撤销</b>，
/// 端点的责任是同时清掉两个 Cookie，否则浏览器会带着一个库里已失效的 token 继续打请求，
/// 表现为"改完密码点什么都 401 但不跳登录页"。
/// </summary>
public sealed record ChangePasswordSuccess : ChangePasswordResult;

/// <summary>
/// 字段级校验失败（400 <c>VALIDATION_FAILED</c>）。键与 <c>openapi</c> 的
/// <c>errors.currentPassword</c> / <c>errors.newPassword</c> 逐字对齐。
/// </summary>
public sealed record ChangePasswordInvalid(IReadOnlyDictionary<string, string[]> Errors) : ChangePasswordResult;

/// <summary>
/// 已认证但账号已不存在（被并发删除/停用）。端点映射为 401 并清 Cookie：
/// 此状态下继续当作"登录着"会让前端停在一个永远 401 的页面上。
/// </summary>
public sealed record ChangePasswordActorGone : ChangePasswordResult;
