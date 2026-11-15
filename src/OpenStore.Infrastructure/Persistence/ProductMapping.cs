using OpenStore.Domain.Catalog;
using OpenStore.Domain.Shared;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// 持久化模型 ↔ 领域模型的唯一映射点（纯函数，无 IO，可单测）。
/// </summary>
/// <remarks>
/// <para>
/// <b>M0 价格形状差异的唯一收敛点</b>：领域侧 <see cref="ProductVariant.Prices"/> 是
/// "币种 → minor units" 的字典（为二期多币种预留），而物理表按数据模型 §D11 只有
/// <c>Currency + AmountMinor</c> 两列。因此：
/// </para>
/// <list type="bullet">
/// <item>读：单列 → 单元素字典。</item>
/// <item>写：字典必须恰好一个元素，否则抛 <c>PRODUCT_PRICE_MULTI_CURRENCY_UNSUPPORTED</c>。</item>
/// </list>
/// <para>
/// 这里选择"写入时报错"而不是"静默丢币种"：丢币种是数据损坏，必须吵出来。
/// 二期落地 <c>VariantPrice</c> 表后，本条限制自然解除。
/// </para>
/// </remarks>
public static class ProductMapping
{
    public static Product ToDomain(ProductRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var variants = record.Variants
            .Select(ToDomainVariant)
            .ToList();

        return Product.Rehydrate(
            record.Id,
            record.Slug,
            record.Title,
            (ProductStatus)record.Status,
            variants);
    }

    /// <summary>把聚合的标量字段写回记录（变体集合的同步见 <see cref="ApplyVariant"/>）。</summary>
    public static void ApplyScalars(Product product, ProductRecord record)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(record);

        record.Id = product.Id;
        record.Slug = product.Slug;
        record.Title = product.Title;
        record.Status = (int)product.Status;
    }

    /// <summary>把变体的可编辑字段（SKU/价格）写回既有记录（Id 不变，不新增行）。</summary>
    public static void ApplyVariant(ProductVariant variant, ProductVariantRecord record)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(record);

        if (variant.Prices.Count > 1)
        {
            throw new DomainException(
                "PRODUCT_PRICE_MULTI_CURRENCY_UNSUPPORTED",
                $"Variant '{variant.Sku}' has prices in {variant.Prices.Count} currencies; " +
                "M0 persists a single currency column (Currency + AmountMinor). " +
                "Multi-currency requires the VariantPrice table planned for phase 2.");
        }

        record.Sku = variant.Sku;
        record.Currency = variant.Prices.Count == 1 ? variant.Prices.First().Key : string.Empty;
        record.AmountMinor = variant.Prices.Count == 1 ? variant.Prices.First().Value : 0L;
    }

    public static ProductVariantRecord ToRecord(ProductVariant variant, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(variant);

        if (variant.Prices.Count > 1)
        {
            throw new DomainException(
                "PRODUCT_PRICE_MULTI_CURRENCY_UNSUPPORTED",
                $"Variant '{variant.Sku}' has prices in {variant.Prices.Count} currencies; " +
                "M0 persists a single currency column (Currency + AmountMinor). " +
                "Multi-currency requires the VariantPrice table planned for phase 2.");
        }

        string currency;
        long amountMinor;
        if (variant.Prices.Count == 1)
        {
            var single = variant.Prices.First();
            currency = single.Key;
            amountMinor = single.Value;
        }
        else
        {
            currency = string.Empty;
            amountMinor = 0L;
        }

        return new ProductVariantRecord
        {
            Id = variant.Id,
            ProductId = productId,
            Sku = variant.Sku,
            AxisSignature = variant.AxisSignature,
            Currency = currency,
            AmountMinor = amountMinor,
        };
    }

    private static ProductVariant ToDomainVariant(ProductVariantRecord record)
    {
        var prices = string.IsNullOrEmpty(record.Currency)
            ? new Dictionary<string, long>(StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal) { [record.Currency] = record.AmountMinor };

        return new ProductVariant(record.Id, record.Sku, record.AxisSignature, prices);
    }
}
