namespace OpenStore.Domain.Inventory;

/// <summary>
/// 库存流水的变化类型。<b>数值一经发布不得改动</b>（数据模型 §3.2）：流水表是只追加的，
/// 枚举数值被写进历史行，改一个数字就等于把过去三个月的账目重新分类。
/// </summary>
public enum StockChangeType
{
    /// <summary>首次入库或建变体时的期初量。</summary>
    Initial = 0,

    /// <summary>后台手工调整（必填原因，US-E2-08）。</summary>
    ManualAdjust = 1,

    /// <summary>下单预占（未支付）。</summary>
    Reserve = 2,

    /// <summary>预占释放（超时/取消，未支付）。</summary>
    Release = 3,

    /// <summary>支付成功：Reserved → Sold，货仍留在仓里。</summary>
    Commit = 4,

    /// <summary>发货：此刻 OnHand 与 Sold 同时减，货真正离仓。</summary>
    Ship = 5,

    /// <summary>退款且已发货：货退回可售池。</summary>
    RefundRestock = 6,

    /// <summary>退款且未发货：只解除 Sold 占用，仓里货没动过。</summary>
    RefundUncommit = 7,
}

/// <summary>流水上的操作者类型。</summary>
public enum StockOperatorType
{
    System = 0,
    Admin = 1,
    Customer = 2,
}

/// <summary>
/// 流水上的操作者快照。<c>Name</c> 是<b>快照</b>而不是外键：管理员改名或离职后，
/// 历史流水仍要能显示"当时是谁调的"，所以不跟随账户变化。
/// </summary>
/// <param name="Type">操作者类别。</param>
/// <param name="Id">操作者主键；系统操作为 <c>null</c>。</param>
/// <param name="Name">操作者显示名快照。</param>
public sealed record StockOperator(StockOperatorType Type, Guid? Id, string? Name)
{
    /// <summary>后台任务、支付回调等无人类操作者的场景。</summary>
    public static StockOperator System { get; } = new(StockOperatorType.System, null, null);

    /// <summary>后台管理员手工操作（调整、发货）。</summary>
    public static StockOperator Admin(Guid adminUserId, string displayName)
        => new(StockOperatorType.Admin, adminUserId, displayName);
}
