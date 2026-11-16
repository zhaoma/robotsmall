namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>CategoryTranslation</c> 表的持久化模型（与领域实体 <see cref="Domain.Catalog.CategoryTranslation"/> 分离）。
/// </summary>
/// <remarks>
/// 唯一键两条：<c>(CategoryId, Locale)</c>（一个分类一个语种只有一行）与 <c>(Locale, Slug)</c>
/// （分类页按 URL 反查的唯一点查路径）。**两条都不过滤软删**（数据模型 §4.3.5 全局策略）：
/// 软删分类的 slug 继续占位，防止新分类复用旧 slug 让旧外链跳到不相干页面。
/// </remarks>
public sealed class CategoryTranslationRecord
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>BCP-47 语言标记（<c>zh-Hans</c> / <c>en</c>），S(10)。</summary>
    public string Locale { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>per-locale 的 slug，写入前统一小写（唯一索引成立的前提）。</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>分类页文案；不设长度上限（PG 上落到 <c>text</c>）。</summary>
    public string? Description { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>译文可编辑 → 有 <c>UpdatedAt</c>（数据模型 §1.5 的可变表规约）。</summary>
    public DateTime UpdatedAt { get; set; }
}
