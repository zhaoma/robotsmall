namespace OpenStore.Domain.Shared;

/// <summary>
/// 全项目唯一金额类型：整数 minor units + 币种代码（后端设计 §2.9）。
/// 不允许在领域层出现 <see cref="decimal"/> 金额 —— 浮点/十进制累加误差在电商里最后一定变成
/// "订单总额比明细之和大 1 分"这类无法解释的账目差异。
/// </summary>
/// <remarks>
/// <para>
/// <b>币种内联在 Money 上，不建币种表</b>（架构 v1.4 已裁定）：单商家部署只跑 1~3 个币种，
/// 币种表带来的 join 与缓存失效成本大于收益。币种的小数位由 <see cref="CurrencyMinorUnits"/> 提供。
/// </para>
/// <para>
/// <b>负数的约束由调用方承担</b>：<see cref="Of"/> 接受负数（行折扣、调价调整都需要），
/// 退款请走 <see cref="Refund"/> 以获得"入参恒为正"的可读性。聚合层负责禁止"订单总额为负"这类语义错误。
/// </para>
/// <para>
/// <c>default(Money)</c> 的 <see cref="Currency"/> 为 <c>null</c>，任何运算都会抛
/// <c>MONEY_CURRENCY_MISMATCH</c>。这是刻意的：漏赋值会立刻炸，而不是当成 0 元静默入账。
/// </para>
/// <para><b>封顶限值</b>：<see cref="long"/> 足够（最大约 9.2e18 minor units）；本类不做额外上限校验。</para>
/// </remarks>
public readonly record struct Money
{
    private Money(long minorUnits, string currency)
    {
        Currency = GuardCurrency(currency);
        MinorUnits = minorUnits;
    }

    /// <summary>最小货币单位（分/日元等零小数位币种的元）。</summary>
    public long MinorUnits { get; }

    /// <summary>ISO 4217 三位大写字母币种代码。</summary>
    public string Currency { get; }

    public bool IsZero => MinorUnits == 0;

    public bool IsNegative => MinorUnits < 0;

    public bool IsPositive => MinorUnits > 0;

    /// <summary>构造金额。允许负值，语义约束见类型注释。</summary>
    public static Money Of(long minorUnits, string currency) => new(minorUnits, currency);

    /// <summary>构造零金额，用于累加起点。</summary>
    public static Money Zero(string currency) => new(0, currency);

    /// <summary>
    /// 构造退款金额：<paramref name="amountMinor"/> 必须为正数，返回的 Money 为负值。
    /// 这样"退款 = 负数入账"，账目自然对冲，不需要在报表里特判退款方向。
    /// </summary>
    public static Money Refund(long amountMinor, string currency)
    {
        if (amountMinor < 0)
        {
            throw new InvariantViolationException(
                "MONEY_REFUND_AMOUNT_INVALID",
                $"Refund amount must be non-negative, got {amountMinor}. Use Money.Of for adjustments.");
        }

        return new Money(-amountMinor, currency);
    }

    /// <summary>由人类可读金额（如 19.99）构造。舍入规则固定为 <see cref="MidpointRounding.AwayFromZero"/>。</summary>
    public static Money FromMajorUnits(decimal amount, string currency)
    {
        var factor = Pow10(CurrencyMinorUnits.For(currency));
        var minor = decimal.Round(amount * factor, 0, MidpointRounding.AwayFromZero);
        return new Money(checked((long)minor), currency);
    }

    /// <summary>转成人类可读金额（仅用于展示与外部接口，禁止用于再计算）。</summary>
    public decimal ToMajorUnits() => MinorUnits / Pow10(CurrencyMinorUnits.For(Currency));

    /// <summary>
    /// 按份数分摊，余数从前往后逐份补 1，<b>各份之和恒等于原值</b>。
    /// 这是折扣/优惠券分摊的基础：10 分钱分 3 份 → 4 / 3 / 3。
    /// </summary>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
        {
            throw new InvariantViolationException(
                "MONEY_ALLOCATE_PARTS_INVALID",
                $"Allocate requires parts >= 1, got {parts}.");
        }

        // C# 整数除法向零截断：-10 / 3 = -3，-10 % 3 = -1 → base * 3 + remainder 恒等于原值（正负都成立）。
        var baseShare = MinorUnits / parts;
        var remainder = MinorUnits % parts;
        var remainderSign = Math.Sign(remainder);
        var remainderAbs = Math.Abs(remainder);

        var result = new Money[parts];
        for (var i = 0; i < parts; i++)
        {
            var bump = i < remainderAbs ? remainderSign : 0L;
            result[i] = new Money(baseShare + bump, Currency);
        }

        return result;
    }

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "add");
        return new Money(checked(left.MinorUnits + right.MinorUnits), left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "subtract");
        return new Money(checked(left.MinorUnits - right.MinorUnits), left.Currency);
    }

    public static Money operator -(Money value) => new(checked(-value.MinorUnits), value.Currency);

    /// <summary>按数量放大。数量为整数：金额永远不做小数乘法（单价 × 1.5 件 = 业务不成立）。</summary>
    public static Money operator *(Money money, int quantity)
        => new(checked(money.MinorUnits * quantity), money.Currency);

    /// <summary>按数量放大（同 <c>Money * int</c>，仅为可读性提供反向写法）。</summary>
    public static Money operator *(int quantity, Money money) => money * quantity;

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    public override string ToString() => $"{MinorUnits} {Currency}";

    private static int Compare(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.MinorUnits.CompareTo(right.MinorUnits);
    }

    private static void EnsureSameCurrency(Money left, Money right, string operation)
    {
        // 用 Ordinal 比较币种代码：'usd' 与 'USD' 绝不能被当成同一币种静默相加。
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                "MONEY_CURRENCY_MISMATCH",
                $"Cannot {operation} money in different currencies: '{left.Currency}' vs '{right.Currency}'.");
        }
    }

    private static string GuardCurrency(string currency)
    {
        if (currency is null || currency.Length != 3)
        {
            throw new InvariantViolationException(
                "MONEY_INVALID_CURRENCY",
                $"Currency must be a 3-letter ISO 4217 code, got '{currency ?? "<null>"}'.");
        }

        foreach (var c in currency)
        {
            if (c is < 'A' or > 'Z')
            {
                throw new InvariantViolationException(
                    "MONEY_INVALID_CURRENCY",
                    $"Currency must be 3 uppercase ASCII letters, got '{currency}'.");
            }
        }

        return currency;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
