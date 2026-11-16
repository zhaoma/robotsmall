namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductTranslation</c> 表的持久化模型（与领域实体 <see cref="Domain.Catalog.ProductTranslation"/> 分离）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是 M0 → M1 唯一一处"数据搬家"的目标表</b>：M0 的 <c>Product.Slug</c>（唯一索引
/// <c>IX_Product_Slug</c>）与 <c>Product.Title</c> 的目标位置就在这里。但<b>本轮不搬</b>——
/// 数据模型 §6.6 的不可逆顺序约束要求"先回填 <c>ProductCategory</c> 并校验通过、再迁 slug、
/// 再切读路径，最后（跨 ≥1 个版本之后）才允许 drop <c>Product.Slug</c>"。
/// </para>
/// <para>
/// <b>两条唯一索引都不过滤软删</b>：软删商品的 slug 继续占位（旧外链的 410/301 由
/// <c>Product.Status=Archived</c> 与 <c>SlugHistory</c> 回答，不靠"释放 slug"）。
/// 软删商品时<b>不得删除译文行</b>：slug 的占位语义完全由本表的 <c>(Locale, Slug)</c> 唯一索引承载。
/// </para>
/// </remarks>
public sealed class ProductTranslationRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>Product</c>，<c>Cascade</c>（纯从属行，数据模型 §5-9 白名单）。</summary>
    public Guid ProductId { get; set; }

    /// <summary>BCP-47 语言标记（<c>zh-Hans</c> / <c>en</c>），S(10)。</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary><c>NOT NULL</c>，但 <c>""</c>/全空白在读取侧一律按"未译"处理（AC-14d①）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>per-locale 的 slug，写入前统一小写；<b>创建后不可改</b>（数据模型 §3.1 终裁 J2）。</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ShortDescription { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
