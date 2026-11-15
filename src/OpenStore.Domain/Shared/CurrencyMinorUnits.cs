namespace OpenStore.Domain.Shared;

/// <summary>
/// 各币种的小数位数（ISO 4217 的 minor unit 定义）。
/// </summary>
/// <remarks>
/// 本期只区分"零小数位"与"两位小数"两类，因为这决定了 <see cref="Money"/> 的 minor↔major 换算。
/// 三位小数的币种（BHD/KWD/OMR/JOD 等）本期不支持：模板目标市场是中东以外，且支持它们
/// 需要把 <see cref="For"/> 与前端展示格式化同时改，属于二期（部署文档 §2 目标市场）。
/// 未列出的币种一律按 2 位处理 <b>并</b> 由 <see cref="For"/> 明确返回 2 —— 不抛异常，
/// 这样新增市场时先能跑通，不会因为漏登记一个币种直接 500。
/// </remarks>
public static class CurrencyMinorUnits
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "JPY", // 日元
        "KRW", // 韩元
    };

    /// <summary>返回该币种的 minor unit 位数（0 或 2）。</summary>
    public static int For(string currency)
    {
        if (string.IsNullOrEmpty(currency))
        {
            throw new InvariantViolationException("MONEY_CURRENCY_REQUIRED", "Currency code is required.");
        }

        return ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
    }
}
