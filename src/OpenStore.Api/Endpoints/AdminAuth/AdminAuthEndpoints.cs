using System.Globalization;
using System.Security.Claims;
using OpenStore.Api.Auth;
using OpenStore.Api.Middlewares;
using OpenStore.Application.Common;
using OpenStore.Application.Customers;
using OpenStore.Application.Customers.Dtos;
using OpenStore.Domain.Customers;

namespace OpenStore.Api.Endpoints.AdminAuth;

/// <summary>
/// 管理端认证端点（登录 / 登出 / me / 改密）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么登录/登出/me 三个端点标 <c>AllowAnonymous</c> 而不是挂权限点</b>：登录是匿名入口；
/// 登出与 me 的"主体是谁"来自 <c>os_admin_sid</c> Cookie（由 <see cref="SessionAuthenticationHandler"/> 填充），
/// 它们校验的是"会话是否有效"，而不是"有没有某个业务操作权限"——挂任何权限点都会让
/// "被停用但还想登出"的账号卡在 403。
/// </para>
/// <para>
/// <b>为什么改密是裸 <c>RequireAuthorization()</c></b>：它改的是"自己的"密码，主体来自
/// <c>ICurrentUser</c>、入参里没有 <c>actorId</c>，因此没有任何资源可以被越权选中。
/// 这是全 Api 唯一允许裸授权的端点，已登记在 <c>AuthorizationTests.裸授权豁免表</c> 里
/// （新增第二条必须走裁决）。
/// </para>
/// <para>
/// <b>本目录不在 <c>Endpoints/Admin/</c> 下</b>：那里要求"每个端点必须挂具体权限点"，
/// 登录入口不满足也不该满足该规则。但<b>不等于本目录不受门禁约束</b>——
/// <c>AuthorizationTests</c> 会扫描整个 <c>Endpoints/</c> 并要求每个端点<b>显式</b>声明
/// 三者之一（权限点 / <c>AllowAnonymous</c> / 豁免表里的裸授权）。
/// 曾经的写法只扫 <c>Endpoints/Admin/</c>，结果是"在本目录里新增一个忘记写授权的端点"
/// 完全不会被任何门禁发现（无 FallbackPolicy 时，没声明的端点默认匿名可访问）。
/// </para>
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
                AdminLoginFailure { Reason: AdminLoginFailureReason.Locked } locked => ProblemResponses.Json(
                    ctx,
                    StatusCodes.Status423Locked,
                    "AUTH_LOCKED",
                    $"Account locked until {locked.UnlockAt:O}."),
                _ => ProblemResponses.Json(
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

            ClearAuthCookies(ctx);
            return Results.NoContent();
        })
        .WithName("AdminLogout")
        .AllowAnonymous()
        .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                return ProblemResponses.Json(ctx, StatusCodes.Status401Unauthorized, "AUTH_SESSION_EXPIRED", "Not authenticated.");
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

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            AdminAuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var result = await auth.ChangePasswordAsync(request, ct).ConfigureAwait(false);

            switch (result)
            {
                case ChangePasswordSuccess:
                    // 成功后该管理员全部会话已撤销（含当前这条）。必须同时清 Cookie，否则浏览器会带着
                    // 一个库里已失效的 token 继续请求：前端看到的是"改完密码后每个接口都 401 但不跳登录页"。
                    ClearAuthCookies(ctx);
                    return Results.NoContent();

                case ChangePasswordInvalid invalid:
                    return ProblemResponses.Json(
                        ctx,
                        StatusCodes.Status400BadRequest,
                        "VALIDATION_FAILED",
                        "密码未通过校验",
                        errors: invalid.Errors);

                default:
                    // 已认证但账号已不存在：清 Cookie 并 401，让前端回到登录页而不是停在一个永远 401 的界面
                    ClearAuthCookies(ctx);
                    return ProblemResponses.Json(
                        ctx,
                        StatusCodes.Status401Unauthorized,
                        "AUTH_SESSION_EXPIRED",
                        "Session no longer valid.");
            }
        })
        .WithName("AdminChangePassword")
        // 已认证即可访问：它改的是"自己的"密码，没有任何业务权限点能更精确地表达这一点。
        // 主体来自 ICurrentUser，入参里根本没有 actorId，因此不存在"改别人密码"的越权面。
        .RequireAuthorization()
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>清掉会话与 CSRF 两个 Cookie（登出、改密成功、账号已不存在时调用）。</summary>
    /// <remarks>
    /// <b><c>Path</c> 必须与写入时一致</b>：浏览器是按 (name, domain, path) 三元组匹配 Cookie 的，
    /// 用默认 <c>Path=/</c> 去删一个 <c>Path=/api/admin</c> 的 Cookie 会静默删不掉，
    /// 而开发者往往因为"库里会话已撤销"而以为删成功了。
    /// </remarks>
    private static void ClearAuthCookies(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(SessionAuthenticationHandler.SessionCookieName, new CookieOptions { Path = "/api/admin" });
        ctx.Response.Cookies.Delete(AdminCsrfMiddleware.CsrfCookieName, new CookieOptions { Path = "/" });
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
