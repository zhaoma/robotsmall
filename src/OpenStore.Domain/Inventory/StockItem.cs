using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Inventory;

/// <summary>
/// 库存聚合根：一个变体一行，三元组计数（OnHand / Reserved / Sold）。
/// </summary>
/// <remarks>
/// <para>
/// <b>三个计数缺一不可，绝不落库 <c>Available</c></b>（数据模型 §3.2）：
/// <list type="number">
/// <item>单一 Available 表达不了"预占"与"已售未发"的区别。释放要区分"预占回退"（不动 OnHand）
/// 与"退款回补"（动 OnHand），否则会出现"取消订单把库存加回去，但其实从没扣过"这类经典 bug。</item>
/// <item>Sold 是发货与退款决策的依据（"这单还有几件没发"、"退回来的货进不进可售池"）。</item>
/// <item>可用量的唯一公式是 <see cref="Available"/> = OnHand − Reserved − Sold，它是<b>计算属性</b>。</item>
/// </list>
/// </para>
/// <para>
/// <b>并发策略</b>：本聚合只负责"给定当前三元组，这个动作是否合法、结果是什么"。
/// 真正的并发保护在持久层——<c>Version</c> 乐观锁 + 单事务。领域层不做锁，
/// 因为"读-判断-写"之间有窗口，领域层无法闭合它；能闭合它的只有 DB 的条件更新。
/// </para>
/// <para>
/// <b>不变量（全部在这里校验，不依赖 DB 约束）</b>：三个计数均 ≥ 0；恒有
/// <c>OnHand ≥ Reserved + Sold</c>。DB 层另有 <c>CHECK</c> 作为"代码写错时的最后一道"，
/// 但"让错误在聚合边界就炸"才是可诊断的做法。
/// </para>
/// </remarks>
public sealed class StockItem
{
    private readonly List<StockMovement> _pending = new();

    private StockItem(Guid variantId, int onHand, int reserved, int sold, long version)
    {
        if (variantId == Guid.Empty)
        {
            throw new InvariantViolationException(
                "STOCK_VARIANT_ID_REQUIRED",
                "StockItem must be bound to a variant; variantId is empty.");
        }

        EnsureCounts(onHand, reserved, sold);
        Version = version;
        VariantId = variantId;
        OnHand = onHand;
        Reserved = reserved;
        Sold = sold;
    }

    /// <summary>变体主键（也是本表的 PK）。</summary>
    public Guid VariantId { get; }

    /// <summary>物理总在库（含已支付未发货——货还在仓里）。</summary>
    public int OnHand { get; private set; }

    /// <summary>已下单未支付的预占量。</summary>
    public int Reserved { get; private set; }

    /// <summary>已支付未发货的占用量。</summary>
    public int Sold { get; private set; }

    /// <summary>乐观锁版本号（字段名就叫 Version，不叫 RowVersion —— 数据模型 §1.4）。</summary>
    public long Version { get; private set; }

    /// <summary>可售量。计算属性，<b>绝不落库</b>。</summary>
    public int Available => OnHand - Reserved - Sold;

    /// <summary>本聚合尚未持久化的流水（由应用层在保存聚合的同一事务里追加到 <c>StockLedger</c>）。</summary>
    public IReadOnlyList<StockMovement> PendingMovements => _pending;

    /// <summary>变体创建时建立库存行；期初量 &gt; 0 时产生一条 <c>Initial</c> 流水。</summary>
    public static StockItem Create(Guid variantId, int initialOnHand, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        var item = new StockItem(variantId, initialOnHand, reserved: 0, sold: 0, version: 0);

        if (initialOnHand > 0)
        {
            item._pending.Add(new StockMovement(
                StockChangeType.Initial,
                OnHandDelta: initialOnHand,
                ReservedDelta: 0,
                SoldDelta: 0,
                OnHandAfter: initialOnHand,
                ReservedAfter: 0,
                SoldAfter: 0,
                Reason: null,
                OrderId: null,
                OrderLineId: null,
                Operator: @operator ?? StockOperator.System,
                OccurredAt: now ?? DateTimeOffset.UtcNow));
        }

        return item;
    }

    /// <summary>
    /// 从持久化层重建。<b>不产生流水</b>，但仍然校验不变量——读出来的数据如果违反
    /// <c>OnHand ≥ Reserved + Sold</c>，宁可炸在读取侧，也不要让脏数据流进下单流程。
    /// </summary>
    public static StockItem Rehydrate(Guid variantId, int onHand, int reserved, int sold, long version)
        => new(variantId, onHand, reserved, sold, version);

    /// <summary>
    /// 后台手工调整（US-E2-08）。
    /// </summary>
    /// <param name="newOnHand">调整后的物理在库总量（不是增量）。</param>
    /// <param name="reason">原因，必填——审计里"谁把 100 改成 3"没有原因等于没有审计。</param>
    /// <param name="operator">操作者。</param>
    /// <param name="now">发生时间。</param>
    /// <remarks>
    /// <b>为什么下限是 <c>Reserved + Sold</c> 而不是 0</b>：已预占与已售出的量在物理上可能
    /// 只是"账面上还在"，但业务上已经属于别人。允许把它们调掉，等于后台一个人就能让已付款订单无货可发。
    /// <b>增量 0 视为幂等并直接返回</b>（不产生空流水）：重复提交一次相同数字不该在流水里多一行。
    /// </remarks>
    public void AdjustOnHand(int newOnHand, string reason, StockOperator @operator, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvariantViolationException(
                "STOCK_ADJUST_REASON_REQUIRED",
                "Manual stock adjustment requires a reason (US-E2-08).");
        }

        if (newOnHand < 0)
        {
            throw new InvariantViolationException(
                "STOCK_ADJUST_NEGATIVE",
                $"OnHand cannot be set to {newOnHand}; negative stock is not representable.");
        }

        var floor = Reserved + Sold;
        if (newOnHand < floor)
        {
            throw new DomainException(
                "INVENTORY_NEGATIVE_FORBIDDEN",
                $"OnHand cannot be adjusted to {newOnHand}: {Reserved} reserved + {Sold} sold = {floor} must stay covered.");
        }

        var delta = newOnHand - OnHand;
        if (delta == 0)
        {
            return;
        }

        OnHand = newOnHand;
        Record(StockChangeType.ManualAdjust, delta, 0, 0, reason.Trim(), orderId: null, orderLineId: null, @operator, now);
    }

    /// <summary>
    /// 下单预占。<b>预占成功才允许建单</b>（US-E5-05）。
    /// </summary>
    /// <exception cref="DomainException">
    /// 可用量不足时抛 <c>INSUFFICIENT_STOCK</c>（映射 409），且<b>不做部分预占</b>：
    /// 要么整车占上，要么一行都不占。部分预占会让"下单失败但库存被吃掉一部分"，是最难排查的一类幽灵占用。
    /// </exception>
    public void Reserve(int quantity, Guid orderId, Guid orderLineId, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(Reserve));

        if (quantity > Available)
        {
            throw new DomainException(
                "INSUFFICIENT_STOCK",
                $"Cannot reserve {quantity}: only {Available} available (OnHand {OnHand}, Reserved {Reserved}, Sold {Sold}).");
        }

        Reserved += quantity;
        Record(StockChangeType.Reserve, 0, quantity, 0, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>释放预占（超时或未支付取消）：只动 Reserved，不动 OnHand。</summary>
    public void Release(int quantity, Guid orderId, Guid orderLineId, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(Release));

        if (quantity > Reserved)
        {
            throw new InvariantViolationException(
                "STOCK_RELEASE_EXCEEDS_RESERVED",
                $"Cannot release {quantity}: only {Reserved} reserved (order line {orderLineId}).");
        }

        Reserved -= quantity;
        Record(StockChangeType.Release, 0, -quantity, 0, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>
    /// 支付成功：Reserved → Sold。<b>货还在仓里</b>，所以 OnHand 不变。
    /// </summary>
    public void Commit(int quantity, Guid orderId, Guid orderLineId, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(Commit));

        if (quantity > Reserved)
        {
            throw new InvariantViolationException(
                "STOCK_COMMIT_EXCEEDS_RESERVED",
                $"Cannot commit {quantity}: only {Reserved} reserved (order line {orderLineId}).");
        }

        Reserved -= quantity;
        Sold += quantity;
        Record(StockChangeType.Commit, 0, -quantity, quantity, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>
    /// 发货：OnHand 与 Sold 同时减 —— <b>此刻货才真正离仓</b>。
    /// </summary>
    public void Ship(int quantity, Guid orderId, Guid orderLineId, StockOperator @operator, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(Ship));

        if (quantity > Sold)
        {
            throw new InvariantViolationException(
                "STOCK_SHIP_EXCEEDS_SOLD",
                $"Cannot ship {quantity}: only {Sold} sold-but-unshipped (order line {orderLineId}).");
        }

        OnHand -= quantity;
        Sold -= quantity;
        Record(StockChangeType.Ship, -quantity, 0, -quantity, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>退款且已发货：货退回可售池（OnHand 增加）。</summary>
    public void RefundRestock(int quantity, Guid orderId, Guid orderLineId, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(RefundRestock));

        OnHand += quantity;
        Record(StockChangeType.RefundRestock, quantity, 0, 0, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>退款且未发货：只解除 Sold 占用，OnHand 一直没动过。</summary>
    public void RefundUncommit(int quantity, Guid orderId, Guid orderLineId, StockOperator? @operator = null, DateTimeOffset? now = null)
    {
        EnsurePositive(quantity, nameof(RefundUncommit));

        if (quantity > Sold)
        {
            throw new InvariantViolationException(
                "STOCK_REFUND_EXCEEDS_SOLD",
                $"Cannot uncommit {quantity}: only {Sold} sold (order line {orderLineId}).");
        }

        Sold -= quantity;
        Record(StockChangeType.RefundUncommit, 0, 0, -quantity, null, orderId, orderLineId, @operator, now);
    }

    /// <summary>取走并清空待持久化流水（应用层在保存聚合后立即写库）。</summary>
    public IReadOnlyList<StockMovement> DrainMovements()
    {
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    private void Record(
        StockChangeType changeType,
        int onHandDelta,
        int reservedDelta,
        int soldDelta,
        string? reason,
        Guid? orderId,
        Guid? orderLineId,
        StockOperator? @operator,
        DateTimeOffset? now)
    {
        EnsureCounts(OnHand, Reserved, Sold);
        Version++;

        _pending.Add(new StockMovement(
            changeType,
            onHandDelta,
            reservedDelta,
            soldDelta,
            OnHandAfter: OnHand,
            ReservedAfter: Reserved,
            SoldAfter: Sold,
            Reason: reason,
            OrderId: orderId,
            OrderLineId: orderLineId,
            Operator: @operator ?? StockOperator.System,
            OccurredAt: now ?? DateTimeOffset.UtcNow));
    }

    private static void EnsureCounts(int onHand, int reserved, int sold)
    {
        if (onHand < 0 || reserved < 0 || sold < 0)
        {
            throw new InvariantViolationException(
                "STOCK_COUNT_NEGATIVE",
                $"Stock counters must be non-negative, got OnHand={onHand}, Reserved={reserved}, Sold={sold}.");
        }

        if (onHand < reserved + sold)
        {
            // AC-08「绝不超卖」的领域层形态：DB 层同形 CHECK 是最后一道，但先在这里炸才能报出是哪个变体
            throw new InvariantViolationException(
                "STOCK_INVARIANT_BROKEN",
                $"OnHand ({onHand}) < Reserved + Sold ({reserved} + {sold}). Stock would go negative.");
        }
    }

    private static void EnsurePositive(int quantity, string operation)
    {
        if (quantity <= 0)
        {
            throw new InvariantViolationException(
                "STOCK_QUANTITY_INVALID",
                $"{operation} requires quantity > 0, got {quantity}.");
        }
    }
}
