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
    IClock clock,
    ICurrentUser currentUser)
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

    /// <summary>
    /// 修改自己的密码（后端设计 §6.7 / INV-AD02 / AC-15）：成功后撤销该管理员<b>全部</b>会话（含当前这条）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>主体只从 <see cref="ICurrentUser"/> 取</b>，入参里没有 <c>actorId</c>——否则"改别人密码"
    /// 只需要改一个请求体字段，而这条路径恰好是所有授权里后果最大的那条。
    /// </para>
    /// <para>
    /// <b>两步写不是原子的，顺序是刻意的</b>：M1 没有 <c>IUnitOfWork</c> 的实现（端口在，实现未落），
    /// 撤销会话是 <c>ExecuteUpdate</c>（立即提交）、改密是 <c>SaveChanges</c>，两者无法同事务。
    /// 因此只能选一个"第二步失败也可接受"的顺序：
    /// </para>
    /// <list type="bullet">
    /// <item><b>先撤销、后改密</b>（本实现）：若保存失败 → 会话全失效、密码仍是旧的。
    /// 用户重新登录、用旧密码即可，安全轴无损。</item>
    /// <item><b>先改密、后撤销</b>：若撤销失败 → 库里是新密码、<b>旧会话仍然有效</b>。
    /// 改密的全部意义就是"把可能被偷的会话踢掉"，这条顺序失败时恰好把唯一想保住的性质丢了，
    /// 而且用户看到 500 会以为"没改成"，实际已经改成了。</item>
    /// </list>
    /// <para>
    /// 哈希在撤销<b>之前</b>算：PBKDF2 100k 迭代是纯 CPU、不碰库，先算可以在不产生任何副作用的前提下失败。
    /// </para>
    /// </remarks>
    public async Task<ChangePasswordResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } actorId)
        {
            return new ChangePasswordActorGone();
        }

        var user = await users.GetByIdAsync(actorId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return new ChangePasswordActorGone();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        // 当前密码错不用 401（契约 D17）：用户已经认证过了，401 会被前端当成"会话过期"踢去登录页，
        // 而真实原因是他在这个输入框里打错了字 —— 字段级提示才对。
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || !passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            errors["currentPassword"] = [PasswordPolicyText.CurrentPasswordMismatch];
        }

        var violations = PasswordPolicy.Validate(request.NewPassword);
        if (violations.Count > 0)
        {
            errors["newPassword"] = PasswordPolicyText.Describe(violations);
        }

        if (errors.Count > 0)
        {
            return new ChangePasswordInvalid(errors);
        }

        var newHash = passwordHasher.Hash(request.NewPassword!);
        var now = clock.UtcNow;

        await sessions
            .RevokeAllForActorAsync(SessionActorType.Admin, actorId, now, SessionRevokeReason.PasswordChanged, ct)
            .ConfigureAwait(false);

        user.ChangePassword(newHash, now);
        await users.SaveAsync(user, ct).ConfigureAwait(false);

        return new ChangePasswordSuccess();
    }
}
