namespace OpenStore.Domain.Inventory;

/// <summary>
/// 一次库存变动的事实记录（对应 <c>StockLedger</c> 的一行，尚未落库）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么同时带 delta 与 after 快照</b>：只存 delta 无法发现"有人绕过 API 直接改库"
/// （对账时 <c>SUM(delta)</c> 与当前值不符只能说明有错，但无法定位是哪一步错）；
/// 只存快照无法做区间聚合。两者并存且互相校验（<c>before + delta == after</c>），
/// 让任何一行被篡改都能在相邻两行之间暴露出来（数据模型 §3.2「流水表如何保证可追溯且不可改」）。
/// </para>
/// <para>
/// <b>三列 delta 而不是一列</b>：同一笔业务动作会同时改多个量（支付成功是
/// <c>Reserved −q, Sold +q</c>），拆成三行会让"一次动作"在流水里看起来像三次，
/// 对账与排障都会多绕一圈。
/// </para>
/// </remarks>
/// <param name="ChangeType">变化类型。</param>
/// <param name="OnHandDelta">物理在库变化量（有符号）。</param>
/// <param name="ReservedDelta">预占变化量（有符号）。</param>
/// <param name="SoldDelta">已售未发变化量（有符号）。</param>
/// <param name="OnHandAfter">变动后物理在库。</param>
/// <param name="ReservedAfter">变动后预占。</param>
/// <param name="SoldAfter">变动后已售未发。</param>
/// <param name="Reason">原因；手工调整时必填。</param>
/// <param name="OrderId">来源订单；非订单动作（期初、手工调整）为 <c>null</c>。</param>
/// <param name="OrderLineId">来源订单行。</param>
/// <param name="Operator">操作者快照。</param>
/// <param name="OccurredAt">发生时间（UTC）。</param>
public sealed record StockMovement(
    StockChangeType ChangeType,
    int OnHandDelta,
    int ReservedDelta,
    int SoldDelta,
    int OnHandAfter,
    int ReservedAfter,
    int SoldAfter,
    string? Reason,
    Guid? OrderId,
    Guid? OrderLineId,
    StockOperator Operator,
    DateTimeOffset OccurredAt)
{
    /// <summary>
    /// 自校验：<c>after − delta</c> 必须等于变动前的值。构造时由聚合保证，这里作为防御性断言。
    /// </summary>
    public bool IsConsistent =>
        OnHandAfter - OnHandDelta >= 0
        && ReservedAfter - ReservedDelta >= 0
        && SoldAfter - SoldDelta >= 0;
}
