using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Ordering;

/// <summary>
/// 订单状态机的<b>白名单</b>式允许转换表（后端设计 §4.1.1）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么是白名单而不是黑名单：黑名单会随着状态新增而漏（新状态默认"什么都能转"），
/// 白名单新增状态时默认"什么都不能转"，故障方向是安全的。
/// </para>
/// <para>
/// <b>本类的非法转换错误码固定为 <see cref="InvalidTransitionCode"/></b>，门禁 V8 会向允许表里
/// 注入 <c>PendingPayment → Shipped</c> 并断言必须抛这个码；因此这个常量是<b>门禁契约</b>，
/// 改名等于把门禁改瞎。
/// </para>
/// </remarks>
public static class OrderStatusMachine
{
    /// <summary>门禁 V8 与集成测试（AC-08）共同断言的错误码。</summary>
    public const string InvalidTransitionCode = "ORDER_INVALID_TRANSITION";

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            // 未支付：只能"付成功"或"取消"（超时取消由后台任务驱动，走同一条边）
            [OrderStatus.PendingPayment] = new[] { OrderStatus.Paid, OrderStatus.Cancelled },

            // 已支付：发货，或全额退款（线下退款/支付渠道退款都把订单直接推到 Refunded）
            [OrderStatus.Paid] = new[] { OrderStatus.Shipped, OrderStatus.Refunded },

            // 已发货：完成（确认收货/自动完成）、或退款（含退货退款）
            [OrderStatus.Shipped] = new[] { OrderStatus.Completed, OrderStatus.Refunded },

            // 已完成：只允许退款（售后），不回到任何"进行中"状态
            [OrderStatus.Completed] = new[] { OrderStatus.Refunded },

            // 终态：没有任何出边
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>(),
            [OrderStatus.Refunded] = Array.Empty<OrderStatus>(),
        };

    /// <summary>全部状态（用于穷举测试与后台任务遍历）。</summary>
    public static IReadOnlyCollection<OrderStatus> AllStatuses { get; } = Enum.GetValues<OrderStatus>();

    /// <summary>纯判定，不抛异常。UI 用它决定按钮可不可点。</summary>
    public static bool IsAllowed(OrderStatus from, OrderStatus to)
    {
        if (!AllowedTransitions.TryGetValue(from, out var targets))
        {
            // 未登记的状态值（含 (OrderStatus)99 这种脏数据）一律视为"没有任何出边"
            return false;
        }

        return Array.IndexOf(targets, to) >= 0;
    }

    /// <summary>返回该状态的合法出边（只读，避免调用方改到内部数组）。</summary>
    public static IReadOnlyList<OrderStatus> AllowedTargets(OrderStatus from)
        => AllowedTransitions.TryGetValue(from, out var targets) ? targets : Array.Empty<OrderStatus>();

    /// <summary>
    /// 执行转换并返回目标状态（便于链式使用）。非法转换抛 <see cref="DomainException"/>，
    /// <see cref="DomainException.Code"/> == <see cref="InvalidTransitionCode"/>。
    /// </summary>
    public static OrderStatus Transition(OrderStatus from, OrderStatus to)
    {
        if (!IsAllowed(from, to))
        {
            throw new DomainException(
                InvalidTransitionCode,
                $"{InvalidTransitionCode}: order status transition '{from}' -> '{to}' is not allowed.");
        }

        return to;
    }
}
