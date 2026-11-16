using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 商品译文（<c>Product</c> 聚合内实体，独立表）。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Slug"/> 创建后不可改</b>（数据模型 §3.1 终裁 J2）：slug 是商品的永久对外地址，
/// 改一次 = 全部外链 404 + SEO 权重清零。因此本类<b>没有</b>改 slug 的方法，管理端 DTO 也不含该字段；
/// 改网址的正确做法是"新建商品 + <c>SlugHistory</c> 301"（M2 落地）。这不是遗漏。
/// </para>
/// <para>
/// <b>唯一索引 <c>(Locale, Slug)</c> 是商品详情页按 URL 反查的唯一点查路径</b>（数据模型 §4.1 第一条），
/// 也是全库最热的一条索引；<c>(ProductId, Locale)</c> 唯一则由"一个商品一个语种只有一行译文"保证。
/// </para>
/// <para>
/// <b>NOT NULL 但允许"未译"</b>：<see cref="Name"/> 是 <c>NOT NULL</c>（译文行一定要有名字），
/// 而 AC-14d① 要求"en 页面存在、某字段为空时回退中文"。因此 <c>""</c>/全空白必须被当作"未译"
/// （写入时 <c>Trim()</c>、读取时 <c>IsNullOrWhiteSpace</c> 判回退），否则该断言在 NOT NULL
/// 约束下永远不可能通过。其余可空字段（Description / SeoTitle / SeoDescription / ShortDescription）
/// 走 <c>NULL</c> 判回退。
/// </para>
/// </remarks>
public sealed class ProductTranslation
{
    private ProductTranslation(
        Guid id,
        Guid productId,
        string locale,
        string name,
        string slug,
        string? description,
        string? shortDescription,
        string? seoTitle,
        string? seoDescription)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("PRODUCT_TRANSLATION_ID_REQUIRED", "ProductTranslation id must not be empty.");
        }

        if (productId == Guid.Empty)
        {
            throw new InvariantViolationException(
                "PRODUCT_TRANSLATION_PRODUCT_REQUIRED",
                "ProductTranslation.ProductId must not be empty.");
        }

        Id = id;
        ProductId = productId;
        Locale = GuardLocale(locale);
        Name = GuardName(name);
        Slug = SlugText.Normalize(slug, "PRODUCT_TRANSLATION");
        Description = TrimToNull(description, maxLength: null);
        ShortDescription = TrimToNull(shortDescription, maxLength: 500);
        SeoTitle = TrimToNull(seoTitle, maxLength: 200);
        SeoDescription = TrimToNull(seoDescription, maxLength: 500);
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    /// <summary>BCP-47 语言标记（<c>zh-Hans</c> / <c>en</c>）。唯一键 <c>(ProductId, Locale)</c> 的一半。</summary>
    public string Locale { get; private set; }

    /// <summary>商品名。写入时 Trim；全空白视为"未译"（见类型注释）。</summary>
    public string Name { get; private set; }

    /// <summary>per-locale 的 URL slug。唯一键 <c>(Locale, Slug)</c>；<b>创建后不可改</b>（终裁 J2）。</summary>
    public string Slug { get; }

    /// <summary>富文本 HTML，入库前已消毒。不设 MaxLength（PG 上落到 <c>text</c>）。</summary>
    public string? Description { get; private set; }

    /// <summary>列表卡片用短描述。</summary>
    public string? ShortDescription { get; private set; }

    /// <summary>空则回退 <see cref="Name"/>（应用层）。</summary>
    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    public static ProductTranslation Create(
        Guid id,
        Guid productId,
        string locale,
        string name,
        string slug,
        string? description = null,
        string? shortDescription = null,
        string? seoTitle = null,
        string? seoDescription = null)
        => new(id, productId, locale, name, slug, description, shortDescription, seoTitle, seoDescription);

    /// <summary>
    /// 改译文文案（不含 slug —— 见类型注释里的终裁 J2）。
    /// </summary>
    public void Update(
        string name,
        string? description,
        string? shortDescription,
        string? seoTitle,
        string? seoDescription)
    {
        Name = GuardName(name);
        Description = TrimToNull(description, maxLength: null);
        ShortDescription = TrimToNull(shortDescription, maxLength: 500);
        SeoTitle = TrimToNull(seoTitle, maxLength: 200);
        SeoDescription = TrimToNull(seoDescription, maxLength: 500);
    }

    private static string GuardLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new InvariantViolationException("PRODUCT_TRANSLATION_LOCALE_REQUIRED", "Locale is required.");
        }

        var trimmed = locale.Trim();
        if (trimmed.Length > 10)
        {
            throw new InvariantViolationException(
                "PRODUCT_TRANSLATION_LOCALE_TOO_LONG",
                $"Locale '{trimmed}' must be <= 10 chars (column limit).");
        }

        return trimmed;
    }

    private static string GuardName(string name)
    {
        var trimmed = TrimToNull(name, maxLength: 200);
        if (trimmed is null)
        {
            throw new InvariantViolationException("PRODUCT_TRANSLATION_NAME_REQUIRED", "Product name is required.");
        }

        return trimmed;
    }

    private static string? TrimToNull(string? value, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (maxLength is { } limit && trimmed.Length > limit)
        {
            throw new InvariantViolationException(
                "PRODUCT_TRANSLATION_FIELD_TOO_LONG",
                $"Value must be <= {limit} chars (column limit), got {trimmed.Length}.");
        }

        return trimmed;
    }
}
