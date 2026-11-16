using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 分类译文（<c>Category</c> 聚合内实体，独立表）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是子表而不是 <c>NameZh/NameEn</c> 两列</b>（数据模型 §3.1）：在 <c>StoreLocale</c>
/// 插一行 = 该语种立即"存在"，译文自动表现为"缺失"（行不存在）→ 新增语种代价 0 张表变更。
/// 加列方案会随语种线性膨胀，第 3 个语种就要改表 + 改所有查询。
/// </para>
/// <para>
/// <b>"有译文"的判据是"存在该 locale 的行"，不是"slug 字符串非空"</b>：slug 是本表的
/// <c>NOT NULL</c> 列，两者在数据上等价，但语义必须用前者表达（用字符串判断会把未来可能
/// 引入的空串 slug 也算成"有译文"）。这也是 <c>translatedIn</c> 的唯一口径。
/// </para>
/// <para>
/// <b>分类的 URL 空间独立于商品</b>：<c>/c/{slug}</c> vs <c>/products/{slug}</c>，所以
/// <c>CategoryTranslation</c> 与 <c>ProductTranslation</c> 的 slug 可以相同，
/// 唯一性只在"同一实体类型内"成立——不要做全局 slug 唯一表。
/// </para>
/// </remarks>
public sealed class CategoryTranslation
{
    private CategoryTranslation(
        Guid id,
        Guid categoryId,
        string locale,
        string name,
        string slug,
        string? description,
        string? seoTitle,
        string? seoDescription)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("CATEGORY_TRANSLATION_ID_REQUIRED", "CategoryTranslation id must not be empty.");
        }

        if (categoryId == Guid.Empty)
        {
            throw new InvariantViolationException(
                "CATEGORY_TRANSLATION_CATEGORY_REQUIRED",
                "CategoryTranslation.CategoryId must not be empty.");
        }

        Id = id;
        CategoryId = categoryId;
        Locale = GuardLocale(locale);
        Name = GuardName(name);
        Slug = SlugText.Normalize(slug, "CATEGORY");
        Description = TrimToNull(description, "CATEGORY_TRANSLATION_DESCRIPTION_TOO_LONG", maxLength: null);
        SeoTitle = TrimToNull(seoTitle, "CATEGORY_TRANSLATION_SEO_TITLE_TOO_LONG", maxLength: 200);
        SeoDescription = TrimToNull(seoDescription, "CATEGORY_TRANSLATION_SEO_DESCRIPTION_TOO_LONG", maxLength: 500);
    }

    public Guid Id { get; }

    public Guid CategoryId { get; }

    /// <summary>BCP-47 语言标记（<c>zh-Hans</c> / <c>en</c>）。唯一键 <c>(CategoryId, Locale)</c> 的一半。</summary>
    public string Locale { get; private set; }

    public string Name { get; private set; }

    /// <summary>分类 URL 的唯一对外标识。唯一键 <c>(Locale, Slug)</c>。写入前统一小写（<see cref="SlugText"/>）。</summary>
    public string Slug { get; private set; }

    public string? Description { get; private set; }

    /// <summary>空则回退 <see cref="Name"/>（应用层回退，不是本类的职责）。</summary>
    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    public static CategoryTranslation Create(
        Guid id,
        Guid categoryId,
        string locale,
        string name,
        string slug,
        string? description = null,
        string? seoTitle = null,
        string? seoDescription = null)
        => new(id, categoryId, locale, name, slug, description, seoTitle, seoDescription);

    // 译文行没有"状态"，改名/改文案都走同一入口：重建实例会重跑全部守卫，
    // 因此不存在"绕过校验的 setter 路径"。
    public void Update(string name, string? description, string? seoTitle, string? seoDescription)
    {
        Name = GuardName(name);
        Description = TrimToNull(description, "CATEGORY_TRANSLATION_DESCRIPTION_TOO_LONG", maxLength: null);
        SeoTitle = TrimToNull(seoTitle, "CATEGORY_TRANSLATION_SEO_TITLE_TOO_LONG", maxLength: 200);
        SeoDescription = TrimToNull(seoDescription, "CATEGORY_TRANSLATION_SEO_DESCRIPTION_TOO_LONG", maxLength: 500);
    }

    /// <summary>
    /// 语言标记校验。<b>不改大小写</b>：<c>zh-Hans</c> 的脚本子标签必须大写，
    /// 一律小写会得到 <c>zh-hans</c>，而它是与配置里 <c>zh-Hans</c> 不等的另一个键。
    /// 规范化交给"写入前必须命中 <c>StoreLocale</c> 集合"这条用例规则（M2 落地）。
    /// </summary>
    private static string GuardLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new InvariantViolationException("CATEGORY_TRANSLATION_LOCALE_REQUIRED", "Locale is required.");
        }

        var trimmed = locale.Trim();
        if (trimmed.Length > 10)
        {
            throw new InvariantViolationException(
                "CATEGORY_TRANSLATION_LOCALE_TOO_LONG",
                $"Locale '{trimmed}' must be <= 10 chars (column limit).");
        }

        return trimmed;
    }

    private static string GuardName(string name)
    {
        var trimmed = TrimToNull(name, "CATEGORY_TRANSLATION_NAME_TOO_LONG", maxLength: 200);
        if (trimmed is null)
        {
            throw new InvariantViolationException("CATEGORY_TRANSLATION_NAME_REQUIRED", "Category name is required.");
        }

        return trimmed;
    }

    /// <summary>
    /// 去空白并把"全空白"折成 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 这一步不是洁癖：AC-14d① 要求"字段为空时回退默认语言并带 <c>data-i18n-fallback</c>"，
    /// 而回退的判据是 <c>string.IsNullOrWhiteSpace</c>。<c>Name</c> 是 <c>NOT NULL</c> 列，
    /// 若允许存 <c>""</c>/全空白，这条断言在数据上永远不可能成立——所以写入侧就必须折叠。
    /// </remarks>
    private static string? TrimToNull(string? value, string tooLongCode, int? maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (maxLength is { } limit && trimmed.Length > limit)
        {
            throw new InvariantViolationException(
                tooLongCode,
                $"Value must be <= {limit} chars (column limit), got {trimmed.Length}.");
        }

        return trimmed;
    }
}
