namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductCategory</c> 表的持久化模型：复合主键 <c>(ProductId, CategoryId)</c>，<b>无独立 Id</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>主分类的 DB 防线是部分唯一索引 <c>UX_ProductCategory_PrimaryPerProduct</c></b>
/// （<c>(ProductId) WHERE IsPrimary</c>）。它只保证"至多一个"；"至少一个"由领域/装载校验保证
/// （见 <see cref="Domain.Catalog.ProductCategory.EnsureExactlyOnePrimaryPerProduct"/>）。
/// 两条合起来才是"恰好一个"，缺任何一条都会安静地退化成"没有面包屑"。
/// </para>
/// <para>
/// <b>本表<b>不是</b>纯从属行</b>：商品被软删（<c>Status=Archived</c>）时<b>不得</b>清空这些行
/// ——"替代分类页"的判定来源就是"归档<b>前</b>所属分类"。物理删商品时由 FK <c>Cascade</c> 清理，
/// 那条路径只出现在测试/重置环境。
/// </para>
/// </remarks>
public sealed class ProductCategoryRecord
{
    public Guid ProductId { get; set; }

    /// <summary>FK → <c>Category</c>，<c>Restrict</c>：有商品归属的分类不得物理删除（数据模型 §1.3 条件硬删）。</summary>
    public Guid CategoryId { get; set; }

    /// <summary>主分类标记。列默认 <c>false</c> —— 所以"忘了设"是默认结果（数据模型 §3.1 的警告）。</summary>
    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
