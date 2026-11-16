using OpenStore.Application.Abstractions;
using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Domain.Catalog;
using OpenStore.Domain.Inventory;
using OpenStore.Domain.Shared;

namespace OpenStore.Application.Catalog.Commands;

/// <summary>
/// 后台商品用例：新建 / 编辑 / 状态变更 / 详情读取。
/// </summary>
/// <remarks>
/// <b>事务边界</b>：M0 的仓储方法（<c>AddAsync</c>/<c>SaveAsync</c>）内部各带一次 <c>SaveChanges</c>，
/// 等效于单聚合单事务（后端设计 §1.5）。M1 引入 <c>IUnitOfWork</c> 后，这里改成"取聚合 → 改 → 由用例统一 Commit"。
/// </remarks>
public sealed class ProductAdminService(
    IProductAdminRepository products,
    IStockRepository stock,
    IClock clock)
{
    /// <summary>取后台商品详情（含变体）。不存在返回 <c>null</c>（端点映射 404）。</summary>
    public async Task<AdminProductDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct).ConfigureAwait(false);
        return product is null ? null : ToDetail(product);
    }

    /// <summary>新建商品（默认 Draft）。Slug 冲突抛 <c>PRODUCT_SLUG_DUPLICATE</c>（400）。</summary>
    public async Task<AdminProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var variants = (request.Variants ?? Array.Empty<CreateProductVariantRequest>())
            .Select(v => new ProductVariant(
                Guid.NewGuid(),
                v.Sku,
                VariantMatrix.BuildSignature(v.Axes),
                new Dictionary<string, long>(StringComparer.Ordinal) { [v.Price.Currency] = v.Price.AmountMinor }))
            .ToList();

        // 先构造聚合：slug/title/变体不变量在 Product.Create 内校验（好报错）
        var product = Product.Create(Guid.NewGuid(), request.Slug, request.Title, variants);

        if (await products.SlugExistsAsync(product.Slug, ct).ConfigureAwait(false))
        {
            throw new DomainException(
                "PRODUCT_SLUG_DUPLICATE",
                $"Product slug '{product.Slug}' already exists.");
        }

        await products.AddAsync(product, ct).ConfigureAwait(false);

        // 每个变体必须有一行库存（数据模型 §3.2：「一变体一行，随变体创建」）。
        // 缺这一行不会报任何错，但该变体在前台永远 available = 0、库存调整接口 404
        // ——商品"建出来了但卖不出去"，是最难自查的一类缺口。
        // 期初量为 0：建品不等于入库，真实到货走 POST /api/admin/inventory/{id}/adjust。
        var now = clock.UtcNow;
        foreach (var variant in product.Variants)
        {
            await stock
                .SaveAsync(StockItem.Create(variant.Id, initialOnHand: 0, StockOperator.System, now), ct)
                .ConfigureAwait(false);
        }

        return ToDetail(product);
    }

    /// <summary>编辑商品（title / 变体 SKU / 变体价格）。商品不存在返回 <c>null</c>（端点映射 404）。</summary>
    public async Task<AdminProductDetailDto?> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await products.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (product is null)
        {
            return null;
        }

        if (request.Title is not null)
        {
            product.UpdateTitle(request.Title);
        }

        if (request.Variants is not null)
        {
            foreach (var variant in request.Variants)
            {
                if (variant.Sku is not null)
                {
                    product.UpdateVariantSku(variant.Id, variant.Sku);
                }

                if (variant.Price is not null)
                {
                    product.UpdateVariantPrice(variant.Id, variant.Price.Currency, variant.Price.AmountMinor);
                }
            }
        }

        await products.SaveAsync(product, ct).ConfigureAwait(false);
        return ToDetail(product);
    }

    /// <summary>
    /// 变更状态。<b>非法状态转换由领域层拒绝</b>（抛 <c>DomainException</c>，端点映射 400），
    /// 本方法只负责"找不到 → null"与"找到 → 改 → 存"。
    /// </summary>
    public async Task<AdminProductStatusDto?> ChangeStatusAsync(Guid productId, ProductStatus target, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(productId, ct).ConfigureAwait(false);
        if (product is null)
        {
            return null;
        }

        // 不变量（能否 Active）在聚合内校验：这里绝不写"if (target == Active && ...)"的重复判断
        product.ChangeStatus(target);
        await products.SaveAsync(product, ct).ConfigureAwait(false);

        return new AdminProductStatusDto(product.Id, product.Slug, product.Status);
    }

    private static AdminProductDetailDto ToDetail(Product product) => new(
        product.Id,
        product.Slug,
        product.Title,
        product.Status,
        product.Variants.Select(ToVariantDto).ToList());

    private static AdminProductVariantDto ToVariantDto(ProductVariant variant)
    {
        AdminMoneyDto? price = null;
        if (variant.HasAnyPrice)
        {
            var (currency, amountMinor) = variant.Prices
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .First();

            price = new AdminMoneyDto(currency, amountMinor);
        }

        return new AdminProductVariantDto(variant.Id, variant.Sku, variant.AxisSignature, price);
    }
}
