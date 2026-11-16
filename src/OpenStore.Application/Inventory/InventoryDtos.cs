namespace OpenStore.Application.Inventory;

/// <summary>
/// <c>POST /api/admin/inventory/{variantId}/adjust</c> 的请求体（契约 <c>Admin.Inventory</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>语义是"设成多少"，不是"加多少"</b>：管理员盘点时货架上数出 7 件就填 7，不需要先心算差值
/// （差值由服务端算出来写进流水）。要求人做减法的地方就会有人算错，而且算错之后流水与实物的偏差
/// 会一路传到对账。<c>newOnHand</c> 这个字段名本身就在表达这件事，不要改成 <c>delta</c>/<c>quantity</c>。
/// </para>
/// <para>
/// <b><see cref="Reason"/> 必填</b>（US-E2-08）：流水表是可追溯的唯一手段，
/// 一条没有原因的 <c>+50</c> 与"有人绕过 API 直接改库"在事后无法区分。
/// </para>
/// </remarks>
/// <param name="NewOnHand">调整后的物理在库总量（含已售未发），必须 ≥ <c>Reserved + Sold</c>。</param>
/// <param name="Reason">调整原因，写进 <c>StockLedger.Reason</c> 与审计日志。</param>
public sealed record AdjustInventoryRequest(int NewOnHand, string Reason);

/// <summary>
/// <c>POST /api/admin/inventory/{variantId}/adjust</c> 的响应体（契约要求 <c>{onHand, available, movementId}</c>）。
/// </summary>
/// <remarks>
/// <b><see cref="MovementId"/> 可空</b>：把数字改成与当前相同的值属于幂等重放，
/// 聚合<b>不写流水</b>（重复提交一次相同数字不该在流水里多一行）。此时返回 <c>null</c> 而不是
/// "最近一条流水的 id" —— 后者会让前端以为这次调整留下了一条记录，排障时按那次 id 去找会找错。
/// </remarks>
/// <param name="OnHand">调整后的物理在库。</param>
/// <param name="Available">可用量 = onHand − reserved − sold。</param>
/// <param name="MovementId">本次写下的流水号；无变化时为 <c>null</c>。</param>
public sealed record AdjustInventoryResponse(int OnHand, int Available, long? MovementId);
