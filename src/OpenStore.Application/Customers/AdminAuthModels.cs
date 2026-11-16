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
