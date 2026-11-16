using OpenStore.Application.Abstractions;
using OpenStore.Application.Common;
using OpenStore.Application.Customers.Dtos;
using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers;

/// <summary>
/// 管理员登录/登出用例（后端设计 §6.3）。
/// </summary>
/// <remarks>
/// <b>为什么失败不抛异常</b>：登录失败的语义是"401 / 423"而不是 400，而全局异常中间件把
/// <c>DomainException</c> 一律映射成 400。登录用结果联合返回，端点再映射成正确的状态码。
/// 锁定阈值与锁定时长由 <see cref="AdminUser"/> 聚合掌握（US-E8-05），本服务只负责编排。
/// </remarks>
public sealed class AdminAuthService(
    IAdminUserRepository users,
    ISessionRepository sessions,
    IPasswordHasher passwordHasher,
    IClock clock)
{
    public async Task<AdminLoginResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new AdminLoginFailure(AdminLoginFailureReason.InvalidCredentials, null);
        }

        var now = clock.UtcNow;
        var user = await users.FindByEmailAsync(AdminUser.NormalizeEmail(email), ct).ConfigureAwait(false);

        if (user is null)
        {
            // 账号不存在也跑一次等成本的哈希（固定成本），防账号枚举（后端设计 §6.3）
            _ = passwordHasher.Hash(request.Password);
            return new AdminLoginFailure(AdminLoginFailureReason.InvalidCredentials, null);
        }

        // 停用账号与"账号不存在/密码错"不区分（防枚举，也不向攻击者透露账号状态）
        if (user.Status == AdminUserStatus.Suspended)
        {
            return new AdminLoginFailure(AdminLoginFailureReason.InvalidCredentials, null);
        }

        if (user.IsLockedOut(now))
        {
            return new AdminLoginFailure(AdminLoginFailureReason.Locked, user.LockoutUntil);
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            user.RegisterFailedLogin(now);
            await users.SaveAsync(user, ct).ConfigureAwait(false);
            return new AdminLoginFailure(AdminLoginFailureReason.InvalidCredentials, null);
        }

        user.RegisterSuccessfulLogin(now);
        await users.SaveAsync(user, ct).ConfigureAwait(false);

        var token = SessionToken.Generate();

        // CSRF 秘钥与会话令牌必须是两个独立随机值：一旦复用同一个值，
        // 攻击者只要拿到其中一个（例如从日志里看到 CSRF 头）就同时掌握了会话令牌。
        var csrfToken = SessionToken.Generate();

        var session = Session.Create(
            SessionToken.Hash(token),
            SessionActorType.Admin,
            user.Id,
            now,
            AdminSessionPolicy.IdleTimeout,
            AdminSessionPolicy.AbsoluteTimeout,
            csrfHash: SessionToken.Hash(csrfToken));

        await sessions.CreateAsync(session, ct).ConfigureAwait(false);

        return new AdminLoginSuccess(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role,
            Permissions.ForRole(user.Role),
            token,
            csrfToken);
    }

    /// <summary>登出：撤销会话。幂等——token 不存在或已撤销都不报错。</summary>
    public Task LogoutAsync(string tokenHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Task.CompletedTask;
        }

        return sessions.RevokeAsync(tokenHash, clock.UtcNow, SessionRevokeReason.Logout, ct);
    }
}
