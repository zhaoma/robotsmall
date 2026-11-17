namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeSet</c> 表：属性集合（如「服装类商品」「数码类商品」）。
/// </summary>
/// <remarks>
/// <para>
/// 集合的作用是<b>定义"某类商品该填哪些属性、哪些必填"</b>：成员关系在
/// <c>AttributeSetMember</c>，分类通过 <c>Category.AttributeSetId</c> 指向它。
/// </para>
/// <para>
/// <b>有效属性集 =（祖先链 <c>AttributeSet.Member</c> 并集）− <c>CategoryAttributeOverride.IsHidden</c></b>
/// （数据模型 §3.1 / US-E2-03）。这个算法要三张表配合，所以本表不存"展开后的结果"——
/// 存了就是第二个真源，父类改成员时子类不会跟着变。
/// </para>
/// <para>
/// 与 <see cref="AttributeGroupRecord"/> 同理，<b>集合名称只在管理端用，不建翻译表</b>。
/// </para>
/// </remarks>
public sealed class AttributeSetRecord
{
    public Guid Id { get; set; }

    /// <summary>稳定业务码，唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
