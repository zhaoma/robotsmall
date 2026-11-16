namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>StockItem</c> 表的持久化模型（一变体一行，随变体创建；与领域聚合 <see cref="Domain.Inventory.StockItem"/> 分离）。
/// </summary>
/// <remarks>
/// <para>
/// <b>只有四个数字列，没有 <c>Available</c></b>：可用量是计算属性
/// （<c>OnHand − Reserved − Sold</c>），<b>永不落库</b>。一旦落库就会与三列不同步，
/// 而"哪个字段是真的"这个问题在盘库时会把账搅成浆糊。
/// </para>
/// <para>
/// <b>三列缺一不可</b>（数据模型 §3.2）：
/// <list type="bullet">
/// <item><see cref="OnHand"/> 物理在库（含"已支付未发货"，货还在仓里）；</item>
/// <item><see cref="Reserved"/> 已下单未支付的预占；</item>
/// <item><see cref="Sold"/> 已支付未发货的占用。</item>
/// </list>
/// 单一 <c>Available</c> 表达不了"锁定"与"已售未发"的区别：释放要区分"预占回退"（不动 OnHand）与
/// "退款回补"（动 OnHand），混在一起就是"取消订单把库存加回去、但其实从没扣过"这类经典 bug。
/// </para>
/// <para>
/// <b>CHECK 约束是"绝不超卖"的最后防线</b>（AC-08）：领域层校验会被并发与重试绕过，
/// 而 <c>CHECK (OnHand >= Reserved + Sold)</c> 在 SQLite 与 PG 上都拦得住——负库存不是"写得不好"，是写不出来。
/// </para>
/// </remarks>
public sealed class StockItemRecord
{
    /// <summary>变体主键，同时是本表 PK（一变体一行）。</summary>
    public Guid VariantId { get; set; }

    /// <summary>物理在库数量（含已支付未发货的货）。</summary>
    public int OnHand { get; set; }

    /// <summary>已下单但未支付的预占量。</summary>
    public int Reserved { get; set; }

    /// <summary>已支付但未发货的占用量（发货时才真正离仓）。</summary>
    public int Sold { get; set; }

    /// <summary>
    /// 乐观锁版本号（统一 <c>long Version</c>，不用 <c>rowversion</c>/<c>xmin</c>：
    /// 后者是 provider 专有，切 SQLite 直接失效）。
    /// </summary>
    public long Version { get; set; }

    /// <summary>建立库存行的时间（数据模型 §1.5：可变表都要有审计尾部）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 最后一次变动时间。后台库存列表要用它显示"最后更新"，契约里的 <c>updatedAt</c> 就是这一列
    /// —— 不建这个列，前端拿到的就是 <c>undefined</c>，而这种缺失不会报错，只会让页面少一行信息。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
