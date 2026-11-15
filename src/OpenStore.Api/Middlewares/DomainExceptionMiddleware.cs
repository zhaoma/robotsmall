using System.Text;
using OpenStore.Domain.Shared;

namespace OpenStore.Api.Middlewares;

/// <summary>
/// 把 <see cref="DomainException"/> 映射成 RFC 7807 <c>application/problem+json</c> + 稳定 <c>code</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>M0 的固定 400 是个真实缺陷，M1 修掉</b>：库存不足、账号锁定、幂等重放这类"请求格式没错、是当前状态不允许"
/// 的情况返回 400，会让前端把一个可重试的冲突当成填错了表单 —— 用户改什么都不对。
/// 状态码按 <see cref="StatusByCode"/> 查表得到，<b>不是 if-else 长链</b>：查表可以一眼看全、可以单测、
/// 加新码只改一行数据。
/// </para>
/// <para>
/// <b>未知码必须能被发现</b>：落 400 的同时打一条 Warning。未知码意味着有人新加了错误码却没登记状态码——
/// 它不应该是"静默按 400 处理"，而应该在上线前被日志和告警抓出来。
/// </para>
/// <para>
/// <b>不在这里 catch (Exception)</b>：其它异常交给框架（开发环境开发者页 / 生产统一 500 + ProblemDetails），
/// 绝不吞掉堆栈。客户端断开（<see cref="OperationCanceledException"/>）也不在这里记 error ——
/// SSR 2 秒超时会大量触发，记 error 会淹没真错误（后端设计 §8.2）。
/// </para>
/// </remarks>
public sealed class DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> logger)
{
    private const string ProblemContentType = "application/problem+json; charset=utf-8";
    private const string ProblemTypePrefix = "https://openstore.dev/errors/";

    /// <summary>
    /// 错误码 → HTTP 状态码。<b>唯一真源</b>：加新错误码时同时在这里登记（后端设计 §8.2 映射表的落地）。
    /// </summary>
    /// <remarks>
    /// 分组顺序即阅读顺序：401/403 是身份问题，409 是"状态不允许"，423 是锁定，400 是"你传的值不对"。
    /// </remarks>
    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        // ---- 401 身份 ----
        ["AUTH_INVALID_CREDENTIALS"] = StatusCodes.Status401Unauthorized,
        ["AUTH_SESSION_EXPIRED"] = StatusCodes.Status401Unauthorized,
        ["AUTH_ACCOUNT_SUSPENDED"] = StatusCodes.Status401Unauthorized,

        // ---- 403 授权 ----
        ["AUTH_FORBIDDEN"] = StatusCodes.Status403Forbidden,
        ["AUTH_CSRF_INVALID"] = StatusCodes.Status403Forbidden,

        // ---- 409 状态冲突（请求没错，是当前状态不允许）----
        ["INSUFFICIENT_STOCK"] = StatusCodes.Status409Conflict,
        ["STOCK_CHANGED_RETRY"] = StatusCodes.Status409Conflict,
        ["INVENTORY_NEGATIVE_FORBIDDEN"] = StatusCodes.Status409Conflict,
        ["CONCURRENT_UPDATE"] = StatusCodes.Status409Conflict,
        ["IDEMPOTENCY_KEY_REUSED"] = StatusCodes.Status409Conflict,
        ["IDEMPOTENCY_IN_PROGRESS"] = StatusCodes.Status409Conflict,
        ["ORDER_INVALID_TRANSITION"] = StatusCodes.Status409Conflict,
        ["SETUP_IN_PROGRESS"] = StatusCodes.Status409Conflict,

        // ---- 423 账号锁定（明确告知何时可试，减少客服）----
        ["AUTH_LOCKED"] = StatusCodes.Status423Locked,

        // ---- 400 入参不合法 ----
        ["VALIDATION_FAILED"] = StatusCodes.Status400BadRequest,
        ["PAGE_OUT_OF_RANGE"] = StatusCodes.Status400BadRequest,
        ["PAGE_SIZE_EXCEEDED"] = StatusCodes.Status400BadRequest,
        ["LOCALE_NOT_ENABLED"] = StatusCodes.Status400BadRequest,
        ["PRODUCT_STATUS_INVALID"] = StatusCodes.Status400BadRequest,
    };

    private static readonly Dictionary<int, string> TitleByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = "提交内容有误",
        [StatusCodes.Status401Unauthorized] = "请重新登录",
        [StatusCodes.Status403Forbidden] = "无权访问",
        [StatusCodes.Status409Conflict] = "当前状态不允许该操作",
        [StatusCodes.Status423Locked] = "账号已临时锁定",
        [StatusCodes.Status429TooManyRequests] = "请求太频繁",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端主动断开：不是错误，不写响应体，只记一条 Information（避免淹没真错误）
            logger.LogInformation(
                "request.aborted {Method} {Path}",
                context.Request.Method,
                context.Request.Path.Value);
        }
        catch (DomainException ex)
        {
            var (status, registered) = ResolveStatus(ex.Code);

            if (!registered && ex.Code.StartsWith(InvariantViolationException.CodePrefix, StringComparison.Ordinal))
            {
                // INV_ 前缀 = 聚合不变量被破坏，属于"状态冲突"而非"你填错了"（后端设计 §8.2）。
                // 它不需要逐个登记状态码：前缀本身就是语义（有人在领域层把聚合改坏了）。
                status = StatusCodes.Status409Conflict;
            }
            else if (!registered)
            {
                // 未登记的错误码：按 400 兜底，但必须吵出来。
                // 这条日志是"有人加了错误码却忘了登记状态码"的唯一发现渠道 —— 它不该被静默按 400 处理。
                logger.LogWarning(
                    "domain.code.unregistered {ErrorCode} -> falling back to 400. " +
                    "Add it to DomainExceptionMiddleware.StatusByCode and to openapi ProblemCode enum.",
                    ex.Code);
            }

            logger.LogWarning(
                ex,
                "Domain rule rejected request: {ErrorCode} -> {Status} {Method} {Path}",
                ex.Code,
                status,
                context.Request.Method,
                context.Request.Path.Value);

            if (context.Response.HasStarted)
            {
                // 已经开始写响应（将来 SSE/流式场景），改不了状态码，只能让它继续炸
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = ProblemContentType;

            var correlationId = context.TraceIdentifier;
            context.Response.Headers["X-Request-Id"] = correlationId;

            await context.Response
                .WriteAsJsonAsync(
                    new ProblemResponse(
                        Type: ProblemTypePrefix + ToKebabCase(ex.Code),
                        Title: TitleByStatus.TryGetValue(status, out var title) ? title : "请求未被接受",
                        Status: status,
                        Detail: ex.Message,
                        Instance: context.Request.Path.Value,
                        Code: ex.Code,
                        CorrelationId: correlationId,
                        Errors: null,
                        Details: null),
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 查表得到状态码，并回报"这个码是否已登记"。
    /// </summary>
    /// <remarks>
    /// 返回值里的 <c>registered</c> 不是冗余：调用方要靠它把"未登记码"单独打一条警告。
    /// 只返回状态码的话，未登记码与正常 400 在日志里长得一模一样，等于没有可观测性。
    /// </remarks>
    private static (int Status, bool Registered) ResolveStatus(string code)
    {
        if (StatusByCode.TryGetValue(code, out var status))
        {
            return (status, true);
        }

        return (StatusCodes.Status400BadRequest, false);
    }

    /// <summary><c>PAGE_OUT_OF_RANGE</c> → <c>page-out-of-range</c>（错误类型 URI 用 kebab-case，便于人读与文档锚点）。</summary>
    private static string ToKebabCase(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var ch in code)
        {
            if (ch == '_')
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 错误响应形状，与契约 <c>#/components/schemas/Problem</c> 一致
    /// （<c>type/title/status/detail/instance/code/correlationId/errors/details</c>）。
    /// </summary>
    private sealed record ProblemResponse(
        string Type,
        string Title,
        int Status,
        string? Detail,
        string? Instance,
        string Code,
        string CorrelationId,
        object? Errors,
        object? Details);
}
