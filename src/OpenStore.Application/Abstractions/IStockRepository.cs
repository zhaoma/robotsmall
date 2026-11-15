using OpenStore.Domain.Inventory;

namespace OpenStore.Application.Abstractions;

/// <summary>
/// 库存视图（admin 库存列表的一行）。
/// </summary>
/// <param name="VariantId">变体主键。</param>
/// <param name="Sku">变体 SKU。</param>
/// <param name="ProductTitle">商品标题（后台看库存时要能认出是哪个商品）。</param>
/// <param name="OnHand">物理在库。</param>
/// <param name="Reserved">预占。</param>
/// <param name="Sold">已售未发。</param>
/// <param name="LowStockThreshold">低库存阈值（来自配置，默认 5）。</param>
public sealed record StockOverviewDto(
    Guid VariantId,
    string Sku,
    string ProductTitle,
    int OnHand,
    int Reserved,
    int Sold,
    int LowStockThreshold)
{
    /// <summary>可用量 = OnHand − Reserved − Sold。<b>计算属性，不落库</b>。</summary>
    public int Available => OnHand - Reserved - Sold;

    /// <summary>是否低库存（仅用于后台筛选与高亮，不影响前台可售性）。</summary>
    public bool IsLowStock => Available <= LowStockThreshold;
}

/// <summary>
/// 库存流水视图（只读投影）。
/// </summary>
/// <param name="Id">流水主键（自增，时间序）。</param>
/// <param name="VariantId">变体主键。</param>
/// <param name="ChangeType">变化类型。</param>
/// <param name="OnHandDelta">物理在库变化。</param>
/// <param name="ReservedDelta">预占变化。</param>
/// <param name="SoldDelta">已售未发变化。</param>
/// <param name="OnHandAfter">变动后在库快照。</param>
/// <param name="ReservedAfter">变动后预占快照。</param>
/// <param name="SoldAfter">变动后已售快照。</param>
/// <param name="Reason">原因（手工调整必有）。</param>
/// <param name="OrderId">来源订单。</param>
/// <param name="OperatorType">操作者类别。</param>
/// <param name="OperatorName">操作者显示名快照。</param>
/// <param name="OccurredAt">发生时间。</param>
public sealed record StockMovementDto(
    long Id,
    Guid VariantId,
    StockChangeType ChangeType,
    int OnHandDelta,
    int ReservedDelta,
    int SoldDelta,
    int OnHandAfter,
    int ReservedAfter,
    int SoldAfter,
    string? Reason,
    Guid? OrderId,
    StockOperatorType OperatorType,
    string? OperatorName,
    DateTimeOffset OccurredAt);

/// <summary>admin 库存列表的筛选条件。</summary>
/// <param name="Query">按 SKU 或商品标题模糊匹配（可选）。</param>
/// <param name="LowStockOnly">只看低库存（可选）。</param>
public sealed record StockListFilter(string? Query, bool LowStockOnly);

/// <summary>
/// 库存仓储端口。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么保存接口接收聚合而不是"三元组 + 期望版本"</b>：并发校验必须由持久层完成
/// （<c>WHERE Version = @original</c>），但"改哪几个量"由聚合决定。若端口只暴露
/// <c>SetCounters(onHand, reserved, sold)</c>，调用方就会自己算增量，等于把
/// "Reserve 是 Reserved +q"这条业务规则抄到用例里，抄漏一处就是超卖。
/// </para>
/// <para>
/// <b>流水必须与三元组同事务写</b>：实现里 <see cref="SaveAsync"/> 要同时
/// ① 条件更新三元组 ② 追加 <c>DrainMovements()</c> 的全部行。任何一步失败都整体回滚——
/// 只写流水不改存量会让对账永久不平，只改存量不写流水则等于"有人绕过 API 改库"。
/// </para>
/// </remarks>
public interface IStockRepository
{
    /// <summary>按变体取库存；不存在返回 <c>null</c>（变体存在但库存行缺失是数据不一致，由调用方决定是否补建）。</summary>
    Task<StockItem?> GetAsync(Guid variantId, CancellationToken ct);

    /// <summary>批量取（前台列表页一次取齐 24 条商品的库存，避免 N+1）。</summary>
    Task<IReadOnlyDictionary<Guid, StockItem>> GetManyAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct);

    /// <summary>
    /// 保存聚合：条件更新三元组 + 追加待持久化流水。
    /// </summary>
    /// <exception cref="OpenStore.Domain.Shared.DomainException">
    /// 版本不匹配（并发写）时抛 <c>CONCURRENT_UPDATE</c>，由中间件映射 409。
    /// </exception>
    Task SaveAsync(StockItem item, CancellationToken ct);

    /// <summary>admin 库存列表（联表取 SKU 与商品标题）。</summary>
    Task<PagedResult<StockOverviewDto>> ListAsync(StockListFilter filter, PageRequest page, CancellationToken ct);

    /// <summary>某变体的流水（按时间倒序，后台排障用）。</summary>
    Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid variantId, PageRequest page, CancellationToken ct);
}
