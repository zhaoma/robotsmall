namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeSetMember</c> 表：属性集合的成员关系。<b>复合主键 <c>(AttributeSetId, AttributeDefinitionId)</c>，无独立 Id</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>复合主键表在 EF 里必须显式 <c>HasKey(x =&gt; new { ... })</c> + <c>ValueGeneratedNever()</c></b>
/// （对照表 §1.2.10）：否则 EF 会试图为第一个属性生成值，插入时得到 <c>Guid.Empty</c> 或自增冲突。
/// </para>
/// <para>
/// <see cref="IsRequired"/> 是"这套属性集下这个属性是否必填"。分类可以通过
/// <c>CategoryAttributeOverride.IsRequired</c>（可空）覆盖它，<c>null</c> 表示"跟随成员设置" ——
/// 这个三态（必填/选填/跟随）不能用两个 bool 表达，所以覆盖表的对应列是可空 bool。
/// </para>
/// </remarks>
public sealed class AttributeSetMemberRecord
{
    public Guid AttributeSetId { get; set; }

    public Guid AttributeDefinitionId { get; set; }

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
