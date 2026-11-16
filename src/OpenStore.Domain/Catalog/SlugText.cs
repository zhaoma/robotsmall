using System.Text.RegularExpressions;
using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// slug 的唯一规则实现（小写、<c>[a-z0-9-]</c>、≤200 字符）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要有这个共享类型，而不是各写一份私有 <c>GuardSlug</c></b>：slug 唯一索引
/// <c>(Locale, Slug)</c> 的成立<b>依赖写入前已规范化</b>（数据模型 §5-1）。两处各写一份正则，
/// 迟早出现"一处小写、一处原样保留"的分叉，而分叉的表现是<b>唯一索引查不到重复却又访问到两个页面</b>
/// —— 库里有 <c>Foo</c> 与 <c>foo</c> 两行，前端按 URL 点查取到哪一行取决于 collation。
/// </para>
/// <para>
/// <b>与 <c>Product.Slug</c> 的关系</b>：M0 的 <see cref="Product"/> 里有一个行为相同的私有
/// <c>GuardSlug</c>。本轮<b>不动它</b>（数据模型 §6.6 的红线：<c>Product.Slug</c> 在分类归属回填
/// 完成、跨 ≥1 个版本之前不得删除，其守卫也随列一起保留）。商品聚合重构（M1 对照表 C14）
/// 时把 <c>Product</c> 收敛到本类型，届时本类就是唯一真源。
/// </para>
/// </remarks>
public static partial class SlugText
{
    /// <summary>列长度上限（<c>ProductTranslation.Slug</c> / <c>CategoryTranslation.Slug</c> 都是 S(200)）。</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// 规范化并校验 slug；不合法抛 <c>{codePrefix}_SLUG_*</c>。
    /// </summary>
    /// <param name="raw">原始输入。</param>
    /// <param name="codePrefix">错误码前缀，如 <c>CATEGORY</c> → <c>INV_CATEGORY_SLUG_INVALID</c>。</param>
    public static string Normalize(string? raw, string codePrefix)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvariantViolationException(
                $"{codePrefix}_SLUG_REQUIRED",
                $"{codePrefix} slug is required.");
        }

        var trimmed = raw.Trim();

        // 先小写再校验：上游（表单、模板、fixture）传上来大小写混写的 slug 是常态，
        // "报错要求人手工改"与"静默规范化"之间选后者——前者会让同一份模板在 SQLite / PG
        // 上表现不同（PG 的 collation 可能让 'A' 与 'a' 相等，SQLite 的 BINARY 不会）。
        var normalized = trimmed.ToLowerInvariant();

        if (normalized.Length > MaxLength || !SlugRegex().IsMatch(normalized))
        {
            throw new InvariantViolationException(
                $"{codePrefix}_SLUG_INVALID",
                $"{codePrefix} slug '{trimmed}' must match ^[a-z0-9]+(-[a-z0-9]+)*$ and be <= {MaxLength} chars " +
                "(normalized to lower case before validation).");
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
