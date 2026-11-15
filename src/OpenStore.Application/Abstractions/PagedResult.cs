namespace OpenStore.Application.Abstractions;

/// <summary>
/// 统一分页信封。字段名与 <c>openapi/openstore.yaml</c> 的 <c>PageMeta</c> 逐字一致：
/// <c>{ items, page, pageSize, total, pageCount }</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>服务端算出 <see cref="PageCount"/> 而不是让前端除</b>：前端各自 <c>Math.ceil(total/pageSize)</c>
/// 会在 <c>total = 0</c> 时得到 <c>0</c> 还是 <c>1</c> 上出现分歧，然后分页组件有的显示"第 1/0 页"。
/// 这类分歧不会报错，只会让两个页面在空结果时长得不一样。
/// </para>
/// <para>
/// <b>不使用 cursor</b>（后端设计 §5.1）：小店数据集 + 后台需要跳页，游标分页让"跳到第 7 页"变成
/// 一次全量遍历。代价是深翻页有 <c>OFFSET</c> 成本，但 <c>page</c> 上限 100 把它封死了。
/// </para>
/// </remarks>
/// <typeparam name="T">条目类型。</typeparam>
/// <param name="Items">当前页条目（可能为空数组，<b>不返回 null</b>）。</param>
/// <param name="Page">当前页码，从 1 开始。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="Total">满足条件的总条数。</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    /// <summary>总页数。<c>total = 0</c> 时为 0（前端据此显示"暂无数据"而不是"第 1/0 页"）。</summary>
    public int PageCount => PageSize <= 0 ? 0 : (Total + PageSize - 1) / PageSize;

    /// <summary>空页（翻过总页数时返回它，<b>不报错</b>：翻过头是用户行为，不是错误）。</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), page, pageSize, 0);
}

/// <summary>分页请求参数（已按 §5.1 校验过的值：<c>page ∈ [1,100]</c>、<c>pageSize ∈ [1,100]</c>）。</summary>
/// <param name="Page">页码，从 1 开始。</param>
/// <param name="PageSize">每页条数，默认 24。</param>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 24;

    public const int MaxPageSize = 100;

    public const int MaxPage = 100;

    /// <summary>跳过的行数。</summary>
    public int Skip => (Page - 1) * PageSize;
}
