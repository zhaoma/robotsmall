namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>Category</c> 表的持久化模型（与领域聚合 <see cref="Domain.Catalog.Category"/> 分离，理由见 <see cref="ProductRecord"/>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>邻接表 + 物化路径两列都在</b>：<see cref="ParentId"/> 服务写与导航树组装（一次全表读
/// <c>ORDER BY ParentId, SortOrder</c>），<see cref="Path"/> 服务"含子树商品"的分类页查询
/// （<c>Path LIKE '/A/%'</c>，索引 <c>IX_Category_Path</c>）。两者由域层维护，<b>禁止任何代码单独改其中一个</b>。
/// </para>
/// <para>
/// <b><see cref="AttributeSetId"/> 本轮只建列、不建外键</b>：目标表 <c>AttributeSet</c> 属属性批次，
/// 本批不建。先建列是为了让后续只需 <c>ADD CONSTRAINT</c>（SQLite 上等价于一次重建表）而不用再
/// <c>ALTER TABLE ADD COLUMN</c>；在没有写入方之前该列恒为 <c>NULL</c>，语义即"继承父链"。
/// </para>
/// </remarks>
public sealed class CategoryRecord
{
    public Guid Id { get; set; }

    /// <summary>父分类；<c>null</c> = 根。FK <c>Restrict</c>（删父级不得连带删子树）。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>物化路径 <c>/{Id}/{Id}/…/</c>（首尾都带斜杠）。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>层级数 = <see cref="Path"/> 的段数（根 = 1）。DB 侧有 <c>CK_Category_Depth</c>。</summary>
    public int Depth { get; set; }

    /// <summary>生效属性集；<c>null</c> = 继承父链。本轮无外键（见类型注释）。</summary>
    public Guid? AttributeSetId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>存 <see cref="Domain.Catalog.CategoryStatus"/> 的数值（不存字符串：改枚举名不该触发数据迁移）。</summary>
    public int Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>乐观锁版本号（数据模型 §1.4）。由 <c>SaveChanges</c> 拦截器维护，映射层不得写它。</summary>
    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
