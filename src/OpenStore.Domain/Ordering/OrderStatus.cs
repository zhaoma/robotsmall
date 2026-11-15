namespace OpenStore.Domain.Ordering;

/// <summary>
/// 订单生命周期状态（三列状态之一，见 <see cref="OrderStatusMachine"/>）。
/// </summary>
public enum OrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Shipped = 2,
    Completed = 3,
    Cancelled = 4,
    Refunded = 5,
}

/// <summary>
/// 履约状态：与支付状态正交。已支付订单可以"部分发货"，此时 <see cref="OrderStatus"/> 仍是 <c>Paid</c>。
/// 把它们压成一个字段是电商系统里最常见的建模错误（"已发货但退款中"无法表达）。
/// </summary>
public enum FulfillmentStatus
{
    Unfulfilled = 0,
    PartiallyFulfilled = 1,
    Fulfilled = 2,
}

/// <summary>
/// 退款状态：与支付状态正交。全额退款会把 <see cref="OrderStatus"/> 推到 <c>Refunded</c>，
/// 部分退款则停留在 <c>PartiallyRefunded</c>（订单仍是 Paid/Shipped/Completed）。
/// </summary>
public enum RefundStatus
{
    None = 0,
    PartiallyRefunded = 1,
    FullyRefunded = 2,
}
