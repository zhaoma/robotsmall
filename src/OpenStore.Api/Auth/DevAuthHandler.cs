using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OpenStore.Api.Auth;

/// <summary>
/// 【M0 开发期占位认证】从请求头 <c>X-Dev-Perms</c> 读权限码，塞进 <c>perm</c> claim。
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ 这是 M0 临时脚手架，M1 必须整类删除。</b>
/// 换成真实实现时：<c>SessionAuthenticationHandler</c>（HttpOnly Cookie + DB Session，后端设计 §6.1/§6.3），
/// 同时删掉本文件、<c>Program.cs</c> 里的 <c>AddScheme</c> 调用、以及 <c>X-Dev-Perms</c> 的全部引用。
/// 保留本类的唯一价值是：在没有任何真实认证实现的情况下，
/// <c>Category=Authorization</c> 门禁与 admin 端点的 401/403 行为仍然可跑、可测、可注入。
/// </para>
/// <para>
/// <b>安全边界</b>：它信任客户端自称的权限码 —— 也就是说，任何能访问到这个进程的人都是超级管理员。
/// 因此它只在 Development 下注册；如果 <c>ASPNETCORE_ENVIRONMENT</c> 被误设成 Production，
/// 这段代码不会自动变得"安全"，部署上必须靠环境变量纪律（部署文档 §4）。
/// </para>
/// </remarks>
public sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>方案名。M1 删除本类时，这个常量一并消失。</summary>
    public const string SchemeName = "DevPermissions";

    /// <summary>权限码 claim 类型（与 <c>Permissions</c> 常量的"值"对应，不是常量名）。</summary>
    public const string PermissionClaimType = "perm";

    /// <summary>
    /// 开发期权限声明头：请求头 <c>X-Dev-Perms</c>，值是 <c>Permissions</c> 里的权限码字面量
    /// （逗号分隔多个）。权限码字面量只允许声明在 <c>Application/Common/Permissions.cs</c>，
    /// 门禁 Category=Authorization 会扫描这一点。
    /// </summary>
    public const string PermissionHeaderName = "X-Dev-Perms";

    public DevAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PermissionHeaderName, out var rawValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var codes = rawValues
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();

        if (codes.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = codes.Select(code => new Claim(PermissionClaimType, code));
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, PermissionClaimType);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
