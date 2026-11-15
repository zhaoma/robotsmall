using System.Security.Claims;
using OpenStore.Api.Auth;
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
                AdminLoginFailure { Reason: AdminLoginFailureReason.Locked } locked => Results.Json(
                    new AuthErrorResponse("AUTH_LOCKED", $"Account locked until {locked.UnlockAt:O}."),
                    statusCode: StatusCodes.Status423Locked),
                _ => Results.Json(
                    new AuthErrorResponse("AUTH_INVALID_CREDENTIALS", "Invalid email or password."),
                    statusCode: StatusCodes.Status401Unauthorized),
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
                return Results.Json(
                    new AuthErrorResponse("AUTH_SESSION_EXPIRED", "Not authenticated."),
                    statusCode: StatusCodes.Status401Unauthorized);
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

            return Results.Ok(new AdminAuthResponse(id, email, displayName, role, permissions));
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

        return Results.Ok(new AdminAuthResponse(
            success.Id,
            success.Email,
            success.DisplayName,
            success.Role,
            success.Permissions));
    }

    /// <summary>登录/me 的失败响应形状（与全局错误中间件保持一致：<c>code</c> + <c>message</c>）。</summary>
    private sealed record AuthErrorResponse(string Code, string Message);
}
