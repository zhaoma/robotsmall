using System.Security.Cryptography;
using System.Text;
using OpenStore.Api.Auth;
using OpenStore.Application.Customers;
using OpenStore.Domain.Shared;

namespace OpenStore.Api.Middlewares;

/// <summary>
/// 管理端 CSRF 防护（<c>Origin</c> 校验 + double-submit cookie）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么 Admin 端要双层，而 Storefront 只做 Origin</b>（后端设计 §6.6）：
/// <c>SameSite=Lax</c> 已经拦住跨站 POST，但拦不住"同站 XSS"与旧浏览器；管理员是高价值目标，
/// 多一层 double-submit 的成本只是一个 Cookie 与一个头部。商城端要兼容"从支付通道 302 回来"
/// 的跨站 GET，加 double-submit 会把 SSR 表单复杂度推高，收益不抵成本——这是显式取舍。
/// </para>
/// <para>
/// <b>为什么从 Claim 里读 CsrfHash 而不是查库</b>：<see cref="SessionAuthenticationHandler"/> 每个请求
/// 都要校会话，已经在内存里拿着 <c>Session.CsrfHash</c>；把它作为 Claim 传下来，本中间件就省掉
/// 一次 DB 往返。Claim 只存在于本次请求的 <see cref="System.Security.Claims.ClaimsPrincipal"/> 上
/// （该 handler 不 <c>SignIn</c>、不写 Cookie），所以不存在"把 CSRF 秘钥写进浏览器"的风险。
/// </para>
/// <para>
/// <b>顺序要求</b>：必须挂在 <c>UseAuthentication</c> 之后（要读 Claim）、
/// 且在 <c>UseAuthorization</c> 之前（无权限的请求没必要再走一遍授权）。
/// 抛 <see cref="DomainException"/> 由最外层的 <c>DomainExceptionMiddleware</c> 映射为 403 + <c>AUTH_CSRF_INVALID</c>。
/// </para>
/// </remarks>
public sealed class AdminCsrfMiddleware(RequestDelegate next, ILogger<AdminCsrfMiddleware> logger)
{
    /// <summary>CSRF double-submit 的 Cookie 名。<b>非 HttpOnly</b>：前端 JS 必须能读它才能放进请求头。</summary>
    public const string CsrfCookieName = "os_admin_csrf";

    /// <summary>CSRF 请求头名。</summary>
    public const string CsrfHeaderName = "X-CSRF-Token";

    private const string AdminPathPrefix = "/api/admin";

    private const string LoginPath = "/api/admin/auth/login";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(AdminPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // ---- ① Origin 校验：所有 admin 请求都做，包括登录（防登录 CSRF） ----
        // GET 也校验：admin 端没有"跨站 GET 拿数据"的正当场景（前端与 API 同源）。
        EnsureSameOrigin(context);

        if (SafeMethods.Contains(context.Request.Method))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // ---- ② 登录端点还没有会话，不要求 CSRF 头（Origin 已在上面校验） ----
        if (context.Request.Path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // ---- ③ double-submit ----
        var expectedHash = context.User.FindFirst(SessionAuthenticationHandler.CsrfHashClaimType)?.Value;
        if (string.IsNullOrEmpty(expectedHash))
        {
            // 没有会话 = 未认证。这里不拦，交给授权管道返回 401：
            // 本中间件若返回 403 会掩盖"你根本没登录"这个更有用的信息。
            await next(context).ConfigureAwait(false);
            return;
        }

        var cookieToken = context.Request.Cookies[CsrfCookieName];
        var headerToken = context.Request.Headers[CsrfHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(headerToken))
        {
            throw new DomainException(
                "AUTH_CSRF_INVALID",
                $"Missing {CsrfHeaderName} header. Every admin write request must echo the '{CsrfCookieName}' cookie value.");
        }

        // 头部与 Cookie 必须一致（double-submit 的本义：攻击者能发请求，但读不到本站 Cookie）
        if (!string.IsNullOrEmpty(cookieToken)
            && !FixedTimeEquals(headerToken, cookieToken))
        {
            logger.LogWarning("admin.csrf.cookie_header_mismatch path={Path}", context.Request.Path.Value);
            throw new DomainException("AUTH_CSRF_INVALID", "CSRF token mismatch between cookie and header.");
        }

        // 与服务器持有的哈希比对：Cookie 可被用户篡改，服务端哈希是唯一权威
        var providedHash = SessionToken.Hash(headerToken);
        if (!FixedTimeEquals(providedHash, expectedHash))
        {
            logger.LogWarning("admin.csrf.hash_mismatch path={Path}", context.Request.Path.Value);
            throw new DomainException("AUTH_CSRF_INVALID", "CSRF token does not match the active session.");
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>Origin</c> 必须是本站。缺失同样拒绝——浏览器同源请求一定会带 <c>Origin</c>，
    /// 不带的基本只有非浏览器客户端，而它们本来就不需要 CSRF 保护，让它们显式带上反而更好排查。
    /// </summary>
    private static void EnsureSameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new DomainException(
                "AUTH_CSRF_INVALID",
                "Missing Origin header. Browser requests send it automatically; CLI callers must pass it explicitly "
                + "(e.g. -H 'Origin: https://your-host').");
        }

        var expected = $"{context.Request.Scheme}://{context.Request.Host}";
        if (!string.Equals(origin.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "AUTH_CSRF_INVALID",
                $"Origin '{origin}' does not match this host '{expected}'.");
        }
    }

    /// <summary>
    /// 定长时间比较。<b>不用 <c>string.Equals</c></b>：它会短路，比较时长随匹配前缀长度变化，
    /// 足以被用来逐字节爆破 token（CSRF token 泄露的后果是攻击者能代替管理员执行写操作）。
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}
