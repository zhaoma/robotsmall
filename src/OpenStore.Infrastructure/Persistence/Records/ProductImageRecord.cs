namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductImage</c> 表：商品/变体与媒体资产的绑定关系。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="VariantId"/> 可空是这张表的核心设计</b>：为空 = 商品级图（所有变体共用），
/// 有值 = 该变体专属图。前台选择变体时的切换逻辑是
/// 「取该变体的图 → 没有则回退到商品级图」，所以不需要为"共用图"复制 N 行。
/// </para>
/// <para>
/// <b><c>Product.PrimaryImageId</c> 与本表构成循环外键</b>（对照表 §3.3）：
/// 保留循环，不做降级 —— 主图指针必须是 <c>Restrict</c>，否则"删图"会让
/// <c>Product.PrimaryImageId</c> 悬空。代价是插入顺序必须是
/// 「建 Product → 建 ProductImage → UPDATE Product SET PrimaryImageId」两段式，
/// 这属于聚合持久化的正常形态。
/// </para>
/// <para>
/// <b><c>AltText</c> 不建翻译表（N1，本轮不做）</b>：02 §9 Q10 定为 Should 级，
/// 且 AC-14 的 DOM 文本遍历不抓 <c>alt</c>，不阻断验收。
/// </para>
/// </remarks>
public sealed class ProductImageRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>Product</c>，<c>Cascade</c>。</summary>
    public Guid ProductId { get; set; }

    /// <summary>FK → <c>ProductVariant</c>，<c>Cascade</c>；空 = 商品级共用图。</summary>
    public Guid? VariantId { get; set; }

    /// <summary>FK → <c>MediaAsset</c>，<c>Restrict</c>：被绑定的资产不得物理删除。</summary>
    public Guid MediaAssetId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>无障碍替代文本（未翻译，M6 视需要再加翻译表）。</summary>
    public string? AltText { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
