using OpenStore.Domain.Ordering;
using OpenStore.Domain.Shared;
using Shouldly;

namespace OpenStore.Domain.Tests.Ordering;

/// <summary>
/// 订单状态机的<b>穷举</b>测试（6×6 = 36 个组合，一个不抽样）。
/// </summary>
/// <remarks>
/// <para>
/// 门禁 V8 会往白名单里注入 <c>PendingPayment → Shipped</c>，并断言因此变红。
/// 为了让"注入后必红"成立，这里的允许表必须是<b>测试自己的独立副本</b>：
/// 如果测试去调用 <see cref="OrderStatusMachine.IsAllowed"/> 拿期望值，
/// 那么无论白名单被改成什么，测试都会跟着一起变绿——这个测试就成了摆设。
/// </para>
/// <para>
/// 本测试的日期/环境无关，纯函数，无 DB（门禁步骤 6 要求领域单测不碰数据库）。
/// </para>
/// </remarks>
public sealed class OrderStatusMachineTests
{
    /// <summary>后端设计 §4.1.1 的允许转换表，逐条硬编码，不从被测代码读取。</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> ExpectedAllowed = new()
    {
        (OrderStatus.PendingPayment, OrderStatus.Paid),
        (OrderStatus.PendingPayment, OrderStatus.Cancelled),
        (OrderStatus.Paid, OrderStatus.Shipped),
        (OrderStatus.Paid, OrderStatus.Refunded),
        (OrderStatus.Shipped, OrderStatus.Completed),
        (OrderStatus.Shipped, OrderStatus.Refunded),
        (OrderStatus.Completed, OrderStatus.Refunded),
    };

    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var all = Enum.GetValues<OrderStatus>();
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in all)
        {
            foreach (var to in all)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void 状态转换_表内放行_表外必抛_ORDER_INVALID_TRANSITION(OrderStatus from, OrderStatus to)
    {
        var expectedAllowed = ExpectedAllowed.Contains((from, to));

        if (expectedAllowed)
        {
            OrderStatusMachine.Transition(from, to).ShouldBe(to);
            OrderStatusMachine.IsAllowed(from, to).ShouldBeTrue($"{from}→{to} 应被允许");
            return;
        }

        var ex = Should.Throw<DomainException>(
            () => OrderStatusMachine.Transition(from, to),
            $"F1 禁止转换未生效：{from}→{to} 未抛 {OrderStatusMachine.InvalidTransitionCode}");

        ex.Code.ShouldBe(
            OrderStatusMachine.InvalidTransitionCode,
            $"F2 错误码不对：{from}→{to} 抛出了 '{ex.Code}'，门禁要求 'ORDER_INVALID_TRANSITION'");

        OrderStatusMachine.IsAllowed(from, to).ShouldBeFalse($"{from}→{to} 不应被允许");
    }

    [Fact]
    public void 允许表规模必须是7条边_白名单被偷偷放宽时本用例先红()
    {
        var actual = Enum.GetValues<OrderStatus>()
            .SelectMany(from => OrderStatusMachine.AllowedTargets(from).Select(to => (from, to)))
            .ToHashSet();

        actual.Count.ShouldBe(7);
        actual.ShouldBe(ExpectedAllowed, ignoreOrder: true);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public void 终态没有任何出边(OrderStatus terminal)
    {
        OrderStatusMachine.AllowedTargets(terminal).ShouldBeEmpty();
    }

    [Fact]
    public void 全部状态都必须在白名单里被显式登记_防止漏配默认放行()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            // 终态返回空集合，非终态返回非空集合；两者都必须是"显式登记过"的结果，
            // 未登记的状态会落到同一个空集合，因此这里额外断言已知终态的数量。
            OrderStatusMachine.AllowedTargets(status).ShouldNotBeNull();
        }

        Enum.GetValues<OrderStatus>()
            .Where(s => OrderStatusMachine.AllowedTargets(s).Count == 0)
            .ShouldBe(new[] { OrderStatus.Cancelled, OrderStatus.Refunded }, ignoreOrder: true);
    }

    [Fact]
    public void 未定义的枚举值视为没有任何出边()
    {
        var bogus = (OrderStatus)99;

        OrderStatusMachine.IsAllowed(bogus, OrderStatus.Paid).ShouldBeFalse();
        Should.Throw<DomainException>(() => OrderStatusMachine.Transition(bogus, OrderStatus.Paid))
            .Code.ShouldBe("ORDER_INVALID_TRANSITION");
    }
}
