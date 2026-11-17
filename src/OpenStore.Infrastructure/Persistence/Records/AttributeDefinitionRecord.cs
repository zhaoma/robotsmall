namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeDefinition</c> 表：一条属性定义（如「颜色」「克重」），属性体系的<b>聚合根</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Code"/> 的领域约束是 <c>^[a-z][a-zA-Z0-9]{1,39}$</c></b>：它会被拼进变量签名
/// （<c>color=red|size=m</c>），所以必须是"无分隔符、无空格、无大小写歧义前缀"的稳定标识。
/// 一旦有属性用了 <c>Color</c> 另一个用了 <c>color</c>，矩阵签名就会出现两种拼法，
/// 而 <c>UX_Variant_Product_AxisSignature</c> 会把它们当成两个不同变体 —— 用户看到"两个红色"。
/// </para>
/// <para>
/// <b>唯一 Code 不过滤软删</b>（与 SKU 同策略）：软删的 <c>color</c> 继续占位，
/// 防止新建一个同名 Code 与历史译文/值表产生语义重叠。
/// </para>
/// <para>
/// <b>不加 <c>Version</c></b>：02 §1.4 的乐观锁清单里没有它（属性定义是低频管理操作）。
/// 若日后出现"两个管理员同时改同一属性被静默覆盖"的投诉，加 <c>Version</c> 是纯增列，成本低。
/// </para>
/// </remarks>
public sealed class AttributeDefinitionRecord
{
    public Guid Id { get; set; }

    /// <summary>稳定业务码，唯一，小写字母开头。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>存 <see cref="Domain.Catalog.AttributeDataType"/> 的数值。</summary>
    public int DataType { get; set; }

    /// <summary>存 <see cref="Domain.Catalog.AttributeScope"/> 的数值。<b>变体轴只能来自 <c>Variant</c> 作用域</b>。</summary>
    public int Scope { get; set; }

    /// <summary>是否可作为前台筛选条件（决定是否进 <c>IX_AttrDef_Filterable_Scope</c> 的筛选查询）。</summary>
    public bool IsFilterable { get; set; }

    /// <summary>单位符号（<c>mm</c> / <c>g</c>）。<b>只做展示，不做换算</b>：换算需要"同量纲基准"，而基准本身是配置，不是这一列能表达的。</summary>
    public string? Unit { get; set; }

    /// <summary>FK → <c>AttributeGroup</c>，<c>Restrict</c>：还有属性归属的分组不得物理删除。</summary>
    public Guid? GroupId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>停用标记（软删但语义是停用，与 <see cref="IsDeleted"/> 不同）。</summary>
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
