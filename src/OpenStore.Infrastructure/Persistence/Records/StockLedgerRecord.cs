namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>StockLedger</c> 表的持久化模型：库存流水的<b>只追加</b>记录（对应数据模型 §3.2）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本表只追加：仓储不得暴露 Update / Delete 方法</b>，也没有 <c>UpdatedAt</c>
/// （不可变表不设此列）。物理上不存在"删一条流水"的语义——那是抹掉证据，而不是修正账目。
/// 需要纠正时追加一条反向流水，让两次动作都留在历史里。PG 生产环境可额外
/// <c>REVOKE UPDATE, DELETE ON "StockLedger" FROM app_role</c>（SQLite 无此能力）。
/// </para>
/// <para>
/// <b>为什么 delta 与 after 快照同时存</b>：只存 delta，对账时 <c>SUM(delta)</c> 与当前值不符只能说明"有错"，
/// 定位不到"哪一步错"；只存快照，做不了区间聚合。两者并存且互相校验
/// （<c>before + delta == after</c>），任何一行被篡改都会在相邻两行之间暴露。
/// </para>
/// <para>
/// <b>三列 delta 而不是一列</b>：一次业务动作会同时改多个量（支付成功是 <c>Reserved −q, Sold +q</c>），
/// 拆成三行会让"一次动作"在流水里看起来像三次，对账与排障都要多绕一圈。
/// </para>
/// <para>
/// <b>外键说明</b>：<c>VariantId → ProductVariant</c> 的 FK 已建（<c>Restrict</c>），与 <see cref="StockItemRecord"/> 一致。
/// 曾经的理由"软删变体要保留历史流水，所以不能建 FK"是<b>错误推论</b>：<c>Restrict</c> 只在物理
/// <c>DELETE</c> 时生效，而软删是 <c>UPDATE IsDeleted=1</c>，两者不冲突；真实风险恰恰相反——
/// 没有 FK 时一旦有人物理删了变体行，流水就成了永远悬空、DB 也不会拦的记录。
/// </para>
/// </remarks>
public sealed class StockLedgerRecord
{
    /// <summary>自增主键（自增即时间序，便于区间聚合与对账）。</summary>
    public long Id { get; set; }

    /// <summary>变体主键。</summary>
    public Guid VariantId { get; set; }

    /// <summary>存 <see cref="Domain.Inventory.StockChangeType"/> 的数值（数值一经发布不得改动）。</summary>
    public int ChangeType { get; set; }

    /// <summary>物理在库变化量（有符号）。</summary>
    public int OnHandDelta { get; set; }

    /// <summary>预占变化量（有符号）。</summary>
    public int ReservedDelta { get; set; }

    /// <summary>已售未发变化量（有符号）。</summary>
    public int SoldDelta { get; set; }

    /// <summary>变动后物理在库快照。</summary>
    public int OnHandAfter { get; set; }

    /// <summary>变动后预占快照。</summary>
    public int ReservedAfter { get; set; }

    /// <summary>变动后已售未发快照。</summary>
    public int SoldAfter { get; set; }

    /// <summary>变动原因。<b>手工调整必填</b>（US-E2-08）：没有原因的 <c>+50</c> 与"有人绕过 API 改库"无法区分。</summary>
    public string? Reason { get; set; }

    /// <summary>来源订单；期初、手工调整等非订单动作为 <c>null</c>。</summary>
    public Guid? OrderId { get; set; }

    /// <summary>来源订单行。</summary>
    public Guid? OrderLineId { get; set; }

    /// <summary>存 <see cref="Domain.Inventory.StockOperatorType"/> 的数值（System / Admin / Customer）。</summary>
    public int OperatorType { get; set; }

    /// <summary>操作者主键；System 动作为 <c>null</c>。</summary>
    public Guid? OperatorId { get; set; }

    /// <summary>操作者显示名快照：人员改名后，历史流水仍还原当时的身份。</summary>
    public string? OperatorName { get; set; }

    /// <summary>关联的请求/任务 id，用于把流水与日志串起来排障。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>发生时间（UTC）。不可变表只有这一个时间列。</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
