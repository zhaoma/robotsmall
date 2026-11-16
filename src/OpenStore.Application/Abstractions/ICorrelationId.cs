namespace OpenStore.Application.Abstractions;

/// <summary>
/// 当前请求的关联标识（<c>StockLedger.CorrelationId</c> 等审计列的写入源）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要这个端口，而不是让仓储自己从 HTTP 上下文取</b>：Infrastructure 不能依赖
/// <c>ASP.NET Core</c>（分层门禁扫描），而"这条流水是哪次请求产生的"必须能落到库里。
/// 端口放在 Application、实现放在 Api，依赖方向仍然是 Api → Application ← Infrastructure。
/// </para>
/// <para>
/// <b>为什么值可能为 <c>null</c></b>：后台任务、迁移、测试宿主没有 HTTP 上下文。
/// 这种情况下宁可留空，也不要编一个（编出来的值会让<em>所有</em>后台写操作看起来来自同一次请求，
/// 排障时比空值更误导）。
/// </para>
/// <para>
/// <b>外部传入的值必须被清洗</b>：本值最终进日志与数据库列，客户端可以随意伪造。
/// 实现方负责白名单字符与长度上界（注入换行符可以伪造日志行，超长串可以把列撑爆）。
/// </para>
/// </remarks>
public interface ICorrelationId
{
    /// <summary>关联标识；无上下文时为 <c>null</c>。</summary>
    string? Value { get; }
}
