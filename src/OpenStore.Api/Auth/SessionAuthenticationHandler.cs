using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenStore.Application.Abstractions;
using OpenStore.Application.Common;
using OpenStore.Application.Customers;

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
    IAdminSessionRepository sessions,
    IClock clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminSession";

    public const string SessionCookieName = "os_admin_sid";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionCookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var admin = await sessions
            .AuthenticateAsync(SessionToken.Hash(token), clock.UtcNow, Context.RequestAborted)
            .ConfigureAwait(false);

        if (admin is null)
        {
            Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/api/admin" });
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email),
            new(ClaimTypes.Name, admin.DisplayName),
            new(ClaimTypes.Role, admin.Role.ToString()),
        };

        foreach (var permission in Permissions.ForRole(admin.Role))
        {
            claims.Add(new Claim(Permissions.PermissionClaimType, permission));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Email, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
