using OpenStore.Application.Catalog.Dtos.Storefront;
using OpenStore.Domain.Catalog;

namespace OpenStore.Application.Catalog.Queries;

/// <summary>
/// 门店商品详情用例（后端设计 §1.2 的第 ③ 段）。
/// </summary>
/// <remarks>
/// <b>M0 的展示价口径（临时、可替换）</b>：在所有"有价格"的变体里，取<b>字典序最小币种</b>，
/// 再取该币种下的<b>最低价</b>作为"起价"。这样做是为了让 M0 只依赖单一 Currency 列就能跑通，
/// 且结果<b>确定</b>（同一份数据永远得到同一个价，测试可断言）。
/// M1 会替换为：locale → 币种解析（Settings 上下文的 StoreSettings.CurrencyMap）+
/// 按 locale 翻译标题 + 库存可用性（IStockQuery）。
/// </remarks>
public sealed class StorefrontProductQueryService(IProductRepository products)
{
    /// <summary>取门店可展示的商品；不存在、未上架、无价格一律返回 <c>null</c>（端点映射成 404，不是 500）。</summary>
    public async Task<StorefrontProductDto?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var product = await products.GetBySlugAsync(slug.Trim(), ct).ConfigureAwait(false);
        if (product is null || product.Status != ProductStatus.Active)
        {
            // 未上架的商品对外表现为"不存在"：不能返回 403，否则等于告诉爬虫"这个 slug 有东西"
            return null;
        }

        var priced = product.Variants.Where(v => v.HasAnyPrice).ToList();
        if (priced.Count == 0)
        {
            return null;
        }

        var currency = priced
            .SelectMany(v => v.Prices.Keys)
            .OrderBy(c => c, StringComparer.Ordinal)
            .First();

        var amountMinor = priced
            .Where(v => v.HasPriceIn(currency))
            .Min(v => v.Prices[currency]);

        return new StorefrontProductDto(
            Slug: product.Slug,
            Title: product.Title,
            Currency: currency,
            AmountMinor: amountMinor,
            IsAvailable: amountMinor > 0);
    }
}
