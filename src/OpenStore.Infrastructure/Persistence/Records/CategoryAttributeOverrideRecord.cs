namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>CategoryAttributeOverride</c> 表：某个分类对继承来的属性做的覆盖。
/// <b>复合主键 <c>(CategoryId, AttributeDefinitionId)</c>，无独立 Id</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么 M1 必建</b>（对照表 §1.0 补充项）：有效属性集 =
/// （祖先链 <c>AttributeSetMember</c> 并集）− <see cref="IsHidden"/>。
/// 没有这张表，M1 的商品属性编辑器只能给出<b>错的</b>必填清单 —— 它会显示父类要求、
/// 但本分类明确不该出现的属性，而且无法隐藏。US-E2-03 明确要求「继承属性只能隐藏不可删」，
/// 这个"只能隐藏"的落点就是这一列。
/// </para>
/// <para>
/// <see cref="IsRequired"/> 是<b>可空</b> bool：<c>null</c> = 跟随 <c>AttributeSetMember.IsRequired</c>，
/// <c>true</c>/<c>false</c> = 本分类强制必填/选填。三态不能用两个 bool 表达。
/// </para>
/// </remarks>
public sealed class CategoryAttributeOverrideRecord
{
    public Guid CategoryId { get; set; }

    public Guid AttributeDefinitionId { get; set; }

    /// <summary>隐藏该属性（继承来的属性不可删，只能隐藏）。</summary>
    public bool IsHidden { get; set; }

    /// <summary>必填覆盖：<c>null</c> = 跟随集合成员设置。</summary>
    public bool? IsRequired { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
