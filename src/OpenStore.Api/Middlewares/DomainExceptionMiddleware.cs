using OpenStore.Domain.Shared;

namespace OpenStore.Api.Middlewares;

/// <summary>
/// 把 <see cref="DomainException"/> 映射成 400 + 稳定错误码，避免领域规则被拒时向上抛成 500。
/// </summary>
/// <remarks>
/// <para>
/// 后端设计 §8.2 的完整版是"RFC 7807 ProblemDetails + 错误码表 + i18n messageKey"。
/// M0 先用最小的 <c>{ code, message, traceId }</c> 形状：字段名与将来的 ProblemDetails
/// 扩展字段（<c>code</c>）保持一致，前端可以先按 <c>code</c> 分支，M1 换包体不会破坏客户端。
/// </para>
/// <para>
/// <b>只捕获领域异常</b>：其它异常交给 ASP.NET Core 的默认处理（开发环境开发者页 / 生产 500），
/// 绝不在这里 catch (Exception) —— 那会吞掉真实的 bug 并让日志失去堆栈。
/// </para>
/// </remarks>
public sealed class DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(
                ex,
                "Domain rule rejected request: {ErrorCode} {Method} {Path}",
                ex.Code,
                context.Request.Method,
                context.Request.Path.Value);

            if (context.Response.HasStarted)
            {
                // 已经开始写响应（例如将来 SSE/流式），无法改状态码，只能让它继续炸
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json; charset=utf-8";

            await context.Response
                .WriteAsJsonAsync(new DomainErrorResponse(ex.Code, ex.Message, context.TraceIdentifier))
                .ConfigureAwait(false);
        }
    }

    /// <summary>M0 的错误响应形状（M1 升级为 ProblemDetails，保留 <c>code</c> 字段）。</summary>
    private sealed record DomainErrorResponse(string Code, string Message, string TraceId);
}
