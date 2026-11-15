using System.Text.RegularExpressions;
using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 商品变体（Product 聚合内实体，独立表）。
/// </summary>
/// <remarks>
/// <b>价格内联在本对象上（字典 key=币种），不建 VariantPrice 子表</b>：
/// 架构 §2.4 / 数据模型 §D11 的结论是"本期少一次热路径 join、二期新增 1:N 表属于纯增量迁移"。
/// M0 的持久化层目前按数据模型的物理形态落成单币种列（<c>Currency + AmountMinor</c>），
/// 因此落库时会校验"只有一个币种"，详见 <c>SqliteProductAdminRepository</c> 的注释。
/// </remarks>
public sealed partial class ProductVariant
{
    private readonly Dictionary<string, long> _prices;

    public ProductVariant(Guid id, string sku, string axisSignature, IReadOnlyDictionary<string, long> prices)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("PRODUCT_VARIANT_ID_REQUIRED", "Variant id must not be empty.");
        }

        Id = id;
        Sku = GuardSku(sku);
        AxisSignature = GuardSignature(axisSignature);
        _prices = GuardPrices(prices);
    }

    public Guid Id { get; }

    /// <summary>商家可读的 SKU（唯一性由 DB 唯一索引保证，不在聚合内校验）。</summary>
    public string Sku { get; }

    /// <summary>轴签名，由 <see cref="VariantMatrix.BuildSignature"/> 生成；同商品内唯一。</summary>
    public string AxisSignature { get; }

    /// <summary>币种 → minor units。只读视图，值不可被外部篡改。</summary>
    public IReadOnlyDictionary<string, long> Prices => _prices;

    public bool HasAnyPrice => _prices.Count > 0;

    public bool HasPriceIn(string currency) => _prices.ContainsKey(currency);

    /// <summary>取指定币种价格；缺失时抛 <c>PRODUCT_VARIANT_NO_PRICE</c>（不要返回 0，0 是"免费"）。</summary>
    public Money PriceIn(string currency)
    {
        if (!_prices.TryGetValue(currency, out var minor))
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_NO_PRICE",
                $"Variant '{Sku}' has no price in currency '{currency}'.");
        }

        return Money.Of(minor, currency);
    }

    private static string GuardSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new InvariantViolationException("PRODUCT_VARIANT_SKU_REQUIRED", "Variant SKU is required.");
        }

        var trimmed = sku.Trim();
        if (trimmed.Length > 64 || !SkuRegex().IsMatch(trimmed))
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_SKU_INVALID",
                $"Variant SKU '{sku}' must match ^[A-Z0-9][A-Z0-9-]{{0,63}}$ (max 64 chars).");
        }

        return trimmed;
    }

    private static string GuardSignature(string axisSignature)
    {
        // 空串是合法值：单规格商品（无颜色无尺码）只有一个变体，它的 AxisSignature 就是空串。
        // 唯一索引 (ProductId, AxisSignature) 因此天然保证"单规格商品最多一个变体"，
        // 不需要在聚合里写特判。null 仍然拒绝——null 表示"忘了调 BuildSignature"，
        // 而空串表示"明确没有轴"，两者必须区分开。
        if (axisSignature is null)
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_AXIS_SIGNATURE_REQUIRED",
                "AxisSignature must not be null; build it with VariantMatrix.BuildSignature.");
        }

        if (axisSignature.Length > 400)
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_AXIS_SIGNATURE_TOO_LONG",
                "AxisSignature must be <= 400 chars (column limit).");
        }

        return axisSignature;
    }

    private static Dictionary<string, long> GuardPrices(IReadOnlyDictionary<string, long>? prices)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (prices is null)
        {
            return result;
        }

        foreach (var (currency, amount) in prices)
        {
            // 币种合法性复用 Money 的守卫（3 位大写字母），避免两套校验规则漂移
            _ = Money.Of(amount, currency);
            if (amount < 0)
            {
                throw new InvariantViolationException(
                    "PRODUCT_VARIANT_PRICE_NEGATIVE",
                    $"Variant price in '{currency}' must be >= 0, got {amount}.");
            }

            result[currency] = amount;
        }

        return result;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuRegex();
}
