using OpenStore.Application.Abstractions;

namespace OpenStore.Api.Auth;

/// <summary>
/// <see cref="ICorrelationId"/> 的 HTTP 实现：优先用调用方带来的 <c>X-Request-Id</c>，
/// 否则用本进程的 <c>TraceIdentifier</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么接受调用方传入的 id</b>：反向代理/前端可能已经在日志里记了一个 id，
/// 不复用它就得靠时间来对齐两条日志——而时间是排障时最不可靠的关联键（时钟偏移、并发写入）。
/// </para>
/// <para>
/// <b>为什么要白名单清洗</b>：这个值会进结构化日志与数据库列，且完全由客户端控制。
/// 不清洗的两个具体后果：① 带 <c>\r\n</c> 的值可以伪造日志行（日志注入）；② 任意长度的值
/// 能把 <c>CorrelationId</c> 列撑到索引放不下。规则取"可打印、无分隔歧义"的最小子集：
/// 字母数字与 <c>- _ . :</c>，长度 1–64（够放 UUID/ULID/W3C traceparent 的尾段）。
/// </para>
/// <para>
/// <b>为什么不合规时静默回退而不是报错</b>：关联 id 是排障设施，不是业务约束。
/// 为一个排障字段拒绝一次真实的下单/调库存请求，是本末倒置。
/// </para>
/// </remarks>
public sealed class HttpCorrelationId(IHttpContextAccessor accessor) : ICorrelationId
{
    /// <summary>调用方传入关联标识用的请求头（与响应头同名，便于前后对照）。</summary>
    public const string HeaderName = "X-Request-Id";

    private const int MaxLength = 64;

    public string? Value
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
            if (IsSafe(incoming))
            {
                return incoming;
            }

            // Kestrel 的 TraceIdentifier 形如 0HNQ3S9ELUBFI:00000001：同一连接内唯一，
            // 足以把"这一次请求"与响应头 X-Request-Id、中间件错误体里的 correlationId 对上。
            return context.TraceIdentifier;
        }
    }

    private static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Length > MaxLength)
        {
            return false;
        }

        foreach (var ch in value)
        {
            var safe = char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':';
            if (!safe)
            {
                return false;
            }
        }

        return true;
    }
}
