using OpenStore.Domain.Shared;
using Shouldly;

namespace OpenStore.Domain.Tests.Shared;

public sealed class MoneyTests
{
    // ---------------------------------------------------------------------
    // Allocate：折扣/优惠券分摊的基础，必须"余数分摊 + 各份之和恒等于原值"
    // ---------------------------------------------------------------------

    [Fact]
    public void Allocate_10分钱分3份_应按余数分摊为4_3_3()
    {
        var shares = Money.Of(10, "USD").Allocate(3);

        shares.Length.ShouldBe(3);
        shares[0].MinorUnits.ShouldBe(4);
        shares[1].MinorUnits.ShouldBe(3);
        shares[2].MinorUnits.ShouldBe(3);
        shares.Sum(s => s.MinorUnits).ShouldBe(10);
    }

    [Fact]
    public void Allocate_各份之和必须恒等于原值_1到99分钱对1到7份穷举()
    {
        for (var total = 1; total <= 99; total++)
        {
            for (var parts = 1; parts <= 7; parts++)
            {
                var shares = Money.Of(total, "USD").Allocate(parts);

                shares.Length.ShouldBe(parts);
                shares.Sum(s => s.MinorUnits).ShouldBe(
                    total,
                    $"total={total}, parts={parts} 分摊后总和不守恒");

                // 任意两份相差不超过 1 分，否则分摊规则就不公平（会把折扣全压在第一份上）
                (shares.Max(s => s.MinorUnits) - shares.Min(s => s.MinorUnits)).ShouldBeLessThanOrEqualTo(1);
            }
        }
    }

    [Fact]
    public void Allocate_负金额_退款分摊也必须守恒()
    {
        var shares = Money.Of(-10, "USD").Allocate(3);

        shares.Sum(s => s.MinorUnits).ShouldBe(-10);
        shares[0].MinorUnits.ShouldBe(-4);
        shares[1].MinorUnits.ShouldBe(-3);
        shares[2].MinorUnits.ShouldBe(-3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_份数必须为正(int parts)
    {
        var ex = Should.Throw<DomainException>(() => Money.Of(10, "USD").Allocate(parts));
        ex.Code.ShouldBe("INV_MONEY_ALLOCATE_PARTS_INVALID");
    }

    // ---------------------------------------------------------------------
    // 币种小数位：JPY/KRW 是零小数位，这是"日元 1000 元"不能变成 10.00 的唯一保证
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("USD", 2)]
    [InlineData("CNY", 2)]
    [InlineData("EUR", 2)]
    public void CurrencyMinorUnits_按币种返回小数位(string currency, int expected)
    {
        CurrencyMinorUnits.For(currency).ShouldBe(expected);
    }

    [Fact]
    public void JPY_零小数位_1000日元就是1000_minor_units()
    {
        var money = Money.FromMajorUnits(1000m, "JPY");

        money.MinorUnits.ShouldBe(1000);
        money.ToMajorUnits().ShouldBe(1000m);
    }

    [Fact]
    public void USD_两位小数_19点99美元是1999_minor_units()
    {
        var money = Money.FromMajorUnits(19.99m, "USD");

        money.MinorUnits.ShouldBe(1999);
        money.ToMajorUnits().ShouldBe(19.99m);
    }

    [Fact]
    public void FromMajorUnits_第三位小数按四舍五入_远离零()
    {
        Money.FromMajorUnits(1.005m, "USD").MinorUnits.ShouldBe(101);
        Money.FromMajorUnits(1.004m, "USD").MinorUnits.ShouldBe(100);
    }

    // ---------------------------------------------------------------------
    // 运算符与跨币种防护
    // ---------------------------------------------------------------------

    [Fact]
    public void 加法_跨币种必须抛异常_不允许静默相加()
    {
        var usd = Money.Of(100, "USD");
        var jpy = Money.Of(100, "JPY");

        var ex = Should.Throw<DomainException>(() => usd + jpy);
        ex.Code.ShouldBe("MONEY_CURRENCY_MISMATCH");
    }

    [Fact]
    public void 比较_跨币种必须抛异常_而不是返回false()
    {
        var usd = Money.Of(100, "USD");
        var jpy = Money.Of(1, "JPY");

        Should.Throw<DomainException>(() => usd > jpy).Code.ShouldBe("MONEY_CURRENCY_MISMATCH");
        Should.Throw<DomainException>(() => usd < jpy).Code.ShouldBe("MONEY_CURRENCY_MISMATCH");
    }

    [Fact]
    public void 加减乘与相等_同币种正常工作()
    {
        var unit = Money.Of(1999, "USD");

        (unit + Money.Of(1, "USD")).MinorUnits.ShouldBe(2000);
        (unit - Money.Of(999, "USD")).MinorUnits.ShouldBe(1000);
        (unit * 3).MinorUnits.ShouldBe(5997);
        (3 * unit).MinorUnits.ShouldBe(5997);
        Money.Of(0, "USD").IsZero.ShouldBeTrue();
        unit.ShouldBe(Money.Of(1999, "USD"));
    }

    [Fact]
    public void Refund_入参为正_结果为负()
    {
        var refund = Money.Refund(500, "USD");

        refund.MinorUnits.ShouldBe(-500);
        refund.IsNegative.ShouldBeTrue();
        (Money.Of(500, "USD") + refund).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Refund_入参为负必须拒绝()
    {
        Should.Throw<DomainException>(() => Money.Refund(-500, "USD")).Code.ShouldBe("INV_MONEY_REFUND_AMOUNT_INVALID");
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U2D")]
    [InlineData("")]
    public void 非法币种代码必须抛出(string currency)
    {
        Should.Throw<DomainException>(() => Money.Of(1, currency)).Code.ShouldBe("INV_MONEY_INVALID_CURRENCY");
    }

    [Fact]
    public void default_Money_参与运算会立刻失败_而不是被当成0元()
    {
        var uninitialized = default(Money);

        Should.Throw<DomainException>(() => uninitialized + Money.Of(1, "USD"))
            .Code.ShouldBe("MONEY_CURRENCY_MISMATCH");
    }
}
