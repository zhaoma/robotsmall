using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenStore.Application.Abstractions;
using OpenStore.Application.Common;
using OpenStore.Application.Customers;
using OpenStore.Domain.Customers;

namespace OpenStore.Api.Auth;

/// <summary>
/// 管理端会话认证处理器：HttpOnly Cookie（<c>os_admin_sid</c>）+ DB Session 校验。
/// </summary>
/// <remarks>
/// <para>
/// Cookie 里只有随机 token，DB 里只有它的 SHA-256（<see cref="SessionToken.Hash"/>）。
/// 校验顺序：token 哈希命中会话 → 会话未撤销/未过期 → 主体是 Admin 且账号 Active →
/// 发 <c>perm</c> claim（由 <see cref="Permissions.ForRole"/> 按角色映射）。
/// </para>
/// <para>
/// 会话失效（撤销/过期/账号停用）时返回 <c>NoResult</c> 并清 Cookie——让浏览器立刻回到登录态，
/// 而不是带着一个坏 Cookie 反复打 401。
/// </para>
/// </remarks>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionRepository sessions,
    IAdminUserRepository users,
    IClock clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminSession";

    public const string SessionCookieName = "os_admin_sid";

    /// <summary>会话 CSRF 秘钥哈希的 Claim 类型（供 <c>AdminCsrfMiddleware</c> double-submit 比对）。</summary>
    /// <remarks>
    /// 放在 Claim 上而不是让中间件再查一次库：处理器每个请求都已经取到 <c>Session</c> 实体，
    /// 再查一次是纯浪费。该 Claim <b>不会</b>被写进 Cookie —— 本处理器只 <c>Authenticate</c>、
    /// 从不 <c>SignIn</c>，ticket 只活在本次请求的内存里。
    /// </remarks>
    public const string CsrfHashClaimType = "os:csrf";

    /// <summary>会话到期时间的 Claim 类型（供 <c>/auth/me</c> 返回 <c>sessionExpiresAt</c>）。</summary>
    public const string SessionExpiresAtClaimType = "os:session_expires_at";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionCookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var tokenHash = SessionToken.Hash(token);
        var now = clock.UtcNow;

        var session = await sessions.FindAsync(tokenHash, Context.RequestAborted).ConfigureAwait(false);
        if (session is null || !session.IsUsable(now) || session.ActorType != SessionActorType.Admin)
        {
            return NoResultAndClearCookie();
        }

        var admin = await users.GetByIdAsync(session.ActorId, Context.RequestAborted).ConfigureAwait(false);
        if (admin is null || admin.Status != AdminUserStatus.Active)
        {
            return NoResultAndClearCookie();
        }

        // 滑动续期（节流的单条条件 UPDATE，见 ISessionRepository.TouchAsync 的约定）
        await sessions
            .TouchAsync(tokenHash, now, now + AdminSessionPolicy.IdleTimeout, now - Session.TouchThrottle, Context.RequestAborted)
            .ConfigureAwait(false);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.DisplayName),
            new(ClaimTypes.Role, admin.Role.ToString()),
        };

        // 会话到期时间：节流写意味着 DB 里的 IdleExpiresAt 可能还没延到本次请求，
        // 因此取"本地值"与"now + idle"的较大者，避免 /auth/me 返回一个已经过去的时间点。
        var sessionExpiresAt = session.IdleExpiresAt > now
            ? session.IdleExpiresAt
            : now + AdminSessionPolicy.IdleTimeout;

        claims.Add(new Claim(SessionExpiresAtClaimType, sessionExpiresAt.ToString("O", CultureInfo.InvariantCulture)));

        if (!string.IsNullOrEmpty(session.CsrfHash))
        {
            claims.Add(new Claim(CsrfHashClaimType, session.CsrfHash));
        }

        foreach (var permission in Permissions.ForRole(admin.Role))
        {
            claims.Add(new Claim(Permissions.PermissionClaimType, permission));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Email, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private AuthenticateResult NoResultAndClearCookie()
    {
        Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/api/admin" });
        return AuthenticateResult.NoResult();
    }
}
