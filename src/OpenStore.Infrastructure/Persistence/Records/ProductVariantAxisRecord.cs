namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductVariantAxis</c> 表：商品的"变体轴定义"（如 轴1=颜色、轴2=尺码）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本表定义"这个商品按哪几个属性切变体"，<c>VariantAttributeValue</c> 记录"每个变体在每根轴上的取值"</b>。
/// 两者不是冗余：前者是<b>商品级</b>的事实（轴顺序决定选择器顺序与签名排序），
/// 后者是<b>变体级</b>的事实（每个变体各取哪个选项）。
/// </para>
/// <para>
/// <b><see cref="SortOrder"/> 的取值域是 1..3，由 CK-8 强制</b>：02 说"轴数 ≤3 无法用 CHECK 表达"
/// 是对的（CHECK 数不了子行），但<b>轴顺序的取值范围可以</b>，而
/// <c>SortOrder = 0</c> 或 <c>9</c> 正是矩阵编辑器最容易写出的脏值 ——
/// 它决定签名里的键排序，脏了就是"同一组变体在不同请求下签名不同"。
/// CK-8 不是"轴数 ≤3"的替代品，别误读。
/// </para>
/// <para>
/// 「轴数 ≤3」仍是<b>领域层不变量</b>（<c>VariantMatrix</c> 保证）。
/// </para>
/// </remarks>
public sealed class ProductVariantAxisRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>Product</c>，<c>Cascade</c>：轴定义是商品的纯从属行，无外部引用。</summary>
    public Guid ProductId { get; set; }

    /// <summary>FK → <c>AttributeDefinition</c>，<c>Restrict</c>：被当轴用的属性定义不得物理删除。</summary>
    public Guid AttributeDefinitionId { get; set; }

    /// <summary>轴顺序，1..3（CK-8）。</summary>
    public int SortOrder { get; set; } = 1;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
