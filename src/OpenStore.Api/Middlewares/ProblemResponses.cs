using System.Text;

namespace OpenStore.Api.Middlewares;

/// <summary>
/// 全站唯一的错误响应形状（RFC 7807 <c>application/problem+json</c>）与状态码标题表。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独抽出来</b>：契约里只有<b>一个</b> <c>Problem</c> schema，前端因此只写一套错误解析。
/// 但产错的地方有两类——中间件（领域异常）与端点（登录失败、字段级校验）——它们各自
/// <c>new</c> 一个匿名对象时，字段名与 <c>type</c> 前缀必然漂移（曾经端点用
/// <c>https://httpstatuses.io/400</c>、中间件用 <c>https://openstore.dev/errors/...</c>，
/// 同一份契约两个 <c>type</c> 域，前端按 <c>type</c> 分支的逻辑直接失效）。
/// 现在两类共用这一个构造器，形状差异在编译期就不成立。
/// </para>
/// <para>
/// <b><c>errors</c> 与 <c>details</c> 永远出现</b>（值为 <c>null</c> 而不是省略）：契约把两者定成
/// nullable 字段，省略与 <c>null</c> 对 <c>JSON.parse</c> 是两件事（前者 <c>undefined</c>），
/// 前端写 <c>if (body.errors)</c> 时两者恰好同效，但一旦有人写 <c>"errors" in body</c> 就会分叉。
/// </para>
/// </remarks>
internal static class ProblemResponses
{
    /// <summary>响应内容类型。带 <c>charset</c>：中文 <c>detail</c> 不带字符集时部分客户端按 latin-1 解。</summary>
    internal const string ContentType = "application/problem+json; charset=utf-8";

    /// <summary>错误类型 URI 前缀（契约示例：<c>https://openstore.dev/errors/validation-failed</c>）。</summary>
    internal const string TypePrefix = "https://openstore.dev/errors/";

    /// <summary>状态码 → 人类可读标题。查表而不是 if 链：可一眼看全、可被契约文档引用。</summary>
    internal static readonly Dictionary<int, string> TitleByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = "提交内容有误",
        [StatusCodes.Status401Unauthorized] = "请重新登录",
        [StatusCodes.Status403Forbidden] = "无权访问",
        [StatusCodes.Status404NotFound] = "未找到",
        [StatusCodes.Status409Conflict] = "当前状态不允许该操作",
        [StatusCodes.Status423Locked] = "账号已临时锁定",
        [StatusCodes.Status429TooManyRequests] = "请求太频繁",
        [StatusCodes.Status503ServiceUnavailable] = "依赖服务不可用",
    };

    /// <summary>错误响应体，字段顺序与契约 <c>#/components/schemas/Problem</c> 一致。</summary>
    internal sealed record Body(
        string Type,
        string Title,
        int Status,
        string? Detail,
        string? Instance,
        string Code,
        string CorrelationId,
        object? Errors,
        object? Details);

    /// <summary>按上下文补齐 <c>instance</c> / <c>correlationId</c> / <c>title</c> / <c>type</c>。</summary>
    internal static Body Build(
        HttpContext context,
        int status,
        string code,
        string? detail,
        object? errors = null,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new Body(
            Type: TypePrefix + ToKebabCase(code),
            Title: TitleByStatus.TryGetValue(status, out var title) ? title : "请求未被接受",
            Status: status,
            Detail: detail,
            Instance: context.Request.Path.Value,
            Code: code,
            CorrelationId: context.TraceIdentifier,
            Errors: errors,
            Details: details);
    }

    /// <summary>
    /// 端点直接返回用。<b>同时写 <c>X-Request-Id</c> 头</b>：中间件产出的错误一直带头，
    /// 端点产出的不带的话，用户截图里的 <c>correlationId</c> 与反代日志里的请求 id 就对不上。
    /// </summary>
    internal static IResult Json(
        HttpContext context,
        int status,
        string code,
        string? detail,
        object? errors = null,
        object? details = null)
    {
        context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;

        return Results.Json(
            Build(context, status, code, detail, errors, details),
            contentType: ContentType,
            statusCode: status);
    }

    /// <summary><c>PAGE_OUT_OF_RANGE</c> → <c>page-out-of-range</c>（错误类型 URI 用 kebab-case，便于人读与文档锚点）。</summary>
    internal static string ToKebabCase(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var ch in code)
        {
            builder.Append(ch == '_' ? '-' : char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
