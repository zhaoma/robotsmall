using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenStore.Api.Auth;
using OpenStore.Api.Middlewares;
using OpenStore.Application.Common;
using OpenStore.Application.Customers;
using OpenStore.Application.Customers.Dtos;
using OpenStore.Domain.Customers;

namespace OpenStore.Api.Endpoints.AdminAuth;

/// <summary>
/// 管理端认证端点（登录 / 登出 / me）。
/// </summary>
/// <remarks>
/// <b>为什么这三个端点不挂 <c>RequireAuthorization</c></b>：登录是匿名入口，登出/me 的"主体是谁"
/// 来自 <c>os_admin_sid</c> Cookie（由 <see cref="SessionAuthenticationHandler"/> 填充），不涉及
/// 具体权限点——它们校验的是"会话是否有效"，而不是"有没有某个业务操作权限"。因此本目录刻意
/// 放在 <c>Endpoints/AdminAuth/</c>（不在 <c>Endpoints/Admin/</c> 下），避免 Category=Authorization
/// 门禁把"该保护的操作没保护"的规则误套到登录入口上。
/// </remarks>
public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/auth")
            .WithTags("Admin.Auth");

        group.MapPost("/login", async (
            LoginRequest request,
            AdminAuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, ct).ConfigureAwait(false);

            return result switch
            {
                AdminLoginSuccess success => LoginSuccess(ctx, success),

                // 失败一律走 ProblemDetails：契约只允许一个 Problem schema，
                // 自造 {code,message} 会让前端写两套错误解析。
                AdminLoginFailure { Reason: AdminLoginFailureReason.Locked } locked => Problem(
                    ctx,
                    StatusCodes.Status423Locked,
                    "AUTH_LOCKED",
                    $"Account locked until {locked.UnlockAt:O}."),
                _ => Problem(
                    ctx,
                    StatusCodes.Status401Unauthorized,
                    "AUTH_INVALID_CREDENTIALS",
                    "Invalid email or password."),
            };
        })
        .WithName("AdminLogin")
        .AllowAnonymous()
        .Produces<AdminAuthResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status423Locked);

        group.MapPost("/logout", async (
            HttpContext ctx,
            AdminAuthService auth,
            CancellationToken ct) =>
        {
            if (ctx.Request.Cookies.TryGetValue(SessionAuthenticationHandler.SessionCookieName, out var token)
                && !string.IsNullOrWhiteSpace(token))
            {
                await auth.LogoutAsync(SessionToken.Hash(token), ct).ConfigureAwait(false);
            }

            ctx.Response.Cookies.Delete(SessionAuthenticationHandler.SessionCookieName, new CookieOptions { Path = "/api/admin" });
            return Results.NoContent();
        })
        .WithName("AdminLogout")
        .AllowAnonymous()
        .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                return Problem(ctx, StatusCodes.Status401Unauthorized, "AUTH_SESSION_EXPIRED", "Not authenticated.");
            }

            var id = Guid.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
                ? parsedId
                : Guid.Empty;

            var email = ctx.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var displayName = ctx.User.FindFirstValue(ClaimTypes.Name) ?? email;

            var role = Enum.TryParse<AdminRole>(ctx.User.FindFirstValue(ClaimTypes.Role), out var parsedRole)
                ? parsedRole
                : AdminRole.Staff;

            var permissions = ctx.User
                .FindAll(Permissions.PermissionClaimType)
                .Select(c => c.Value)
                .ToList();

            // 到期时间由 SessionAuthenticationHandler 作为 Claim 带下来（它已经查过会话，
            // 不必为这一个字段再查一次库）。解析失败给 null 而不是当前时间：
            // 前端据此显示"会话即将过期"横幅，给一个假时间会诱导错误判断。
            DateTimeOffset? sessionExpiresAt = null;
            if (DateTimeOffset.TryParse(
                    ctx.User.FindFirstValue(SessionAuthenticationHandler.SessionExpiresAtClaimType),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedExpiry))
            {
                sessionExpiresAt = parsedExpiry;
            }

            return Results.Ok(new AdminMeResponse(id, email, displayName, role, permissions, sessionExpiresAt));
        })
        .WithName("AdminMe")
        .AllowAnonymous()
        .Produces<AdminAuthResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult LoginSuccess(HttpContext ctx, AdminLoginSuccess success)
    {
        ctx.Response.Cookies.Append(
            SessionAuthenticationHandler.SessionCookieName,
            success.SessionToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/api/admin",
            });

        // CSRF double-submit：这个 Cookie 必须可被 JS 读取（HttpOnly=false），
        // 前端读出后放进 X-CSRF-Token 头。Path=/ 而非 /api/admin：
        // 管理端 SPA 挂在 /admin 下，若限定 Path=/api/admin，前端页面自己读不到它。
        ctx.Response.Cookies.Append(
            AdminCsrfMiddleware.CsrfCookieName,
            success.CsrfToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });

        return Results.Ok(new AdminAuthResponse(
            success.Id,
            success.Email,
            success.DisplayName,
            success.Role,
            success.Permissions));
    }

    /// <summary>
    /// 统一错误形状：<c>application/problem+json</c>，字段与契约 <c>Problem</c> 逐字对齐。
    /// </summary>
    /// <remarks>
    /// 这里不抛 <c>DomainException</c> 的原因：登录失败的语义是 401/423，而全局异常中间件
    /// 对未登记码默认 400；用结果联合 + 显式映射能让状态码在端点里一眼可见，
    /// 但<b>响应体形状必须与中间件产出的完全一致</b>，否则前端要写两套解析。
    /// </remarks>
    private static IResult Problem(HttpContext ctx, int status, string code, string detail)
        => Results.Json(
            new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{status}",
                Title = ReasonPhrases.GetReasonPhrase(status),
                Status = status,
                Detail = detail,
                Instance = ctx.Request.Path,
                Extensions =
                {
                    ["code"] = code,
                    ["correlationId"] = ctx.TraceIdentifier,
                },
            },
            contentType: "application/problem+json",
            statusCode: status);
}

/// <summary>
/// <c>/api/admin/auth/me</c> 的响应。比登录响应多一个 <c>sessionExpiresAt</c>：
/// 前端据此在临近过期时提示"会话即将结束"，而不是等下一次写操作突然 401。
/// </summary>
/// <param name="Id">管理员主键。</param>
/// <param name="Email">邮箱（原样展示）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Role">角色。</param>
/// <param name="Permissions">权限点集合（仅用于 UI 隐藏菜单，服务端策略才是权威）。</param>
/// <param name="SessionExpiresAt">会话空闲到期时间（UTC）；取不到时为 <c>null</c>。</param>
internal sealed record AdminMeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    AdminRole Role,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? SessionExpiresAt);
