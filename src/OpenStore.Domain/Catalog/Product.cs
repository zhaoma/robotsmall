using System.Text.RegularExpressions;
using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 商品聚合根。
/// </summary>
/// <remarks>
/// <para>
/// <b>不变量</b>（构造函数与状态变更处校验，不依赖 DB 约束）：
/// <list type="number">
/// <item>Slug 匹配 <c>^[a-z0-9]+(-[a-z0-9]+)*$</c> —— 它是 SEO 的唯一入口，脏 slug 无法事后修复（旧链接已外发）。</item>
/// <item>至少一个变体；"单规格商品"= 一个变体，不是零个变体。</item>
/// <item>同商品下变体 <see cref="ProductVariant.AxisSignature"/> 不重复（DB 有唯一索引，域层提前拦，好报错）。</item>
/// <item>只有"每个变体都至少有一档价格"的商品才能 Active（否则前台出现无价商品 = 转化率黑洞）。</item>
/// </list>
/// </para>
/// <para>
/// <b>为什么没有 Category / 图片 / 库存导航属性</b>：它们属于其它聚合或值集合，
/// 通过 Id 引用（ProductCategory 表、跨上下文端口 <c>IStockQuery</c>），不进本聚合的可变状态。
/// </para>
/// </remarks>
public sealed partial class Product
{
    private readonly List<ProductVariant> _variants;

    private Product(Guid id, string slug, string title, ProductStatus status, IEnumerable<ProductVariant> variants)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("PRODUCT_ID_REQUIRED", "Product id must not be empty.");
        }

        Id = id;
        Slug = GuardSlug(slug);
        Title = GuardTitle(title);
        _variants = GuardVariants(variants);
        Status = status;

        if (status == ProductStatus.Active)
        {
            EnsureActivatable();
        }
    }

    public Guid Id { get; }

    /// <summary>URL slug（小写、连字符分隔）。DB 另有唯一索引（不过滤软删，见数据模型 §D4）。</summary>
    public string Slug { get; }

    public string Title { get; private set; }

    public ProductStatus Status { get; private set; }

    public IReadOnlyList<ProductVariant> Variants => _variants;

    /// <summary>新建商品（默认 Draft —— 不给"新建即可售"的捷径）。</summary>
    public static Product Create(Guid id, string slug, string title, IEnumerable<ProductVariant> variants)
        => new(id, slug, title, ProductStatus.Draft, variants);

    /// <summary>
    /// 从持久化层重建聚合。与 <see cref="Create"/> 的唯一区别是允许直接指定状态，
    /// 但仍然<b>完整校验不变量</b>：读出来的数据如果违反了领域规则，宁可炸在读取侧，
    /// 也不要让脏数据悄悄流到下单流程里。
    /// </summary>
    public static Product Rehydrate(Guid id, string slug, string title, ProductStatus status, IEnumerable<ProductVariant> variants)
        => new(id, slug, title, status, variants);

    /// <summary>修改商品标题（admin PATCH 端点走这里）。校验与构造时一致，防止绕过守卫改出脏数据。</summary>
    public void UpdateTitle(string title)
    {
        Title = GuardTitle(title);
    }

    /// <summary>
    /// 修改某个变体的 SKU。变体不可变（<see cref="ProductVariant"/> 无 setter），
    /// 这里用"同 Id 重建实例"的方式替换，保持聚合内实体不可变与不变量校验完整。
    /// </summary>
    public void UpdateVariantSku(Guid variantId, string sku)
    {
        var index = IndexOfVariant(variantId);
        var current = _variants[index];
        // 先按新 SKU 重建（复用 ProductVariant 的格式守卫），再对整集复查唯一性：
        // 只重建单变体不过聚合守卫，会给"改 SKU"留一条绕过口（错误码退化成裸 DB 异常）。
        // 复查用的 helper 与 GuardVariants 同一个，改 SKU 与建品不可能分叉出两套判重。
        var updated = new ProductVariant(current.Id, sku, current.AxisSignature, current.Prices);
        var candidate = new List<ProductVariant>(_variants) { [index] = updated };
        EnsureUniqueSkus(candidate);
        _variants[index] = updated;
    }

    /// <summary>
    /// 修改某个变体在指定币种下的价格（覆盖该币种、保留其余币种）。
    /// M0 物理表只存单币种列，改出多币种会在持久化层抛
    /// <c>PRODUCT_PRICE_MULTI_CURRENCY_UNSUPPORTED</c>（见 <c>ProductMapping</c>）。
    /// </summary>
    public void UpdateVariantPrice(Guid variantId, string currency, long amountMinor)
    {
        var index = IndexOfVariant(variantId);
        var current = _variants[index];

        var prices = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (code, amount) in current.Prices)
        {
            prices[code] = amount;
        }

        prices[currency] = amountMinor;

        // 重建实例以复用 ProductVariant 的价格守卫（币种合法性 / 非负）
        _variants[index] = new ProductVariant(current.Id, current.Sku, current.AxisSignature, prices);
    }

    private int IndexOfVariant(Guid variantId)
    {
        var index = _variants.FindIndex(v => v.Id == variantId);
        if (index < 0)
        {
            throw new DomainException(
                "PRODUCT_VARIANT_NOT_FOUND",
                $"Variant '{variantId}' not found in product '{Slug}'.");
        }

        return index;
    }

    /// <summary>状态变更唯一入口（admin PATCH 端点走这里）。非法目标状态抛 <c>PRODUCT_CANNOT_ACTIVATE_*</c>。</summary>
    public void ChangeStatus(ProductStatus target)
    {
        if (!Enum.IsDefined(target))
        {
            // 越界枚举值（请求体里写 99）必须在聚合边界拦住，否则会以脏数据落库
            throw new InvariantViolationException(
                "PRODUCT_STATUS_INVALID",
                $"'{target}' is not a defined ProductStatus.");
        }

        if (Status == target)
        {
            return; // 幂等：重复 PATCH 同一状态不报错
        }

        if (target == ProductStatus.Active)
        {
            EnsureActivatable();
        }

        Status = target;
    }

    private void EnsureActivatable()
    {
        if (_variants.Count == 0)
        {
            throw new InvariantViolationException(
                "PRODUCT_CANNOT_ACTIVATE_NO_VARIANT",
                $"Product '{Slug}' has no variant and cannot be activated.");
        }

        var noPrice = _variants.FirstOrDefault(v => !v.HasAnyPrice);
        if (noPrice is not null)
        {
            throw new InvariantViolationException(
                "PRODUCT_CANNOT_ACTIVATE_VARIANT_WITHOUT_PRICE",
                $"Product '{Slug}' variant '{noPrice.Sku}' has no price and cannot be activated.");
        }
    }

    private static List<ProductVariant> GuardVariants(IEnumerable<ProductVariant>? variants)
    {
        var list = variants?.ToList() ?? new List<ProductVariant>();

        var duplicate = list
            .GroupBy(v => v.AxisSignature, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_SIGNATURE_DUPLICATE",
                $"Product has duplicate variant axis signature '{duplicate.Key}'.");
        }

        var duplicateId = list.GroupBy(v => v.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvariantViolationException(
                "PRODUCT_VARIANT_ID_DUPLICATE",
                $"Product has duplicate variant id '{duplicateId.Key}'.");
        }

        EnsureUniqueSkus(list);

        return list;
    }

    /// <summary>
    /// SKU 唯一守卫。建品/重建（<see cref="GuardVariants"/>）与改 SKU（<see cref="UpdateVariantSku"/>）
    /// 共用这一处，改两条路径不可能分叉出两套判重逻辑。
    /// </summary>
    /// <remarks>
    /// SKU 唯一是硬契约（数据模型 §6.1 UX_Variant_Sku 唯一、不依赖 collation）。
    /// 领域层做前置守卫给友好错误码，DB 唯一索引兜底防并发绕过，二者职责不同不是重复。
    /// Ordinal 与 DB 索引字节序一致（大小写敏感，SKU 规范要求大写，见 ProductVariant 守卫）。
    /// </remarks>
    private static void EnsureUniqueSkus(IReadOnlyList<ProductVariant> variants)
    {
        var duplicateSku = variants
            .GroupBy(v => v.Sku, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateSku is not null)
        {
            throw new InvariantViolationException(
                "PRODUCT_SKU_DUPLICATE",
                $"Product has duplicate variant SKU '{duplicateSku.Key}'.");
        }
    }

    private static string GuardSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvariantViolationException("PRODUCT_SLUG_REQUIRED", "Product slug is required.");
        }

        if (slug.Length > 200 || !SlugRegex().IsMatch(slug))
        {
            throw new InvariantViolationException(
                "PRODUCT_SLUG_INVALID",
                $"Product slug '{slug}' must match ^[a-z0-9]+(-[a-z0-9]+)*$ and be <= 200 chars.");
        }

        return slug;
    }

    private static string GuardTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvariantViolationException("PRODUCT_TITLE_REQUIRED", "Product title is required.");
        }

        var trimmed = title.Trim();
        if (trimmed.Length > 300)
        {
            throw new InvariantViolationException(
                "PRODUCT_TITLE_TOO_LONG",
                "Product title must be <= 300 chars (column limit).");
        }

        return trimmed;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}

