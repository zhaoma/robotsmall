namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductAttributeValue</c> 表：<b>商品级</b>属性值的窄表（属性一行一个值）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是窄表而不是"每个属性一列"或"一个大 JSON 列"</b>：
/// 属性由管理员自定义（M5 模板还会加），列在编译期未知 —— 宽表需要 DDL 变更；
/// 大 JSON 列则让"按颜色=白筛选"退化成全表扫 + 应用层解析，且无法建索引。
/// 窄表的代价是<b>一次查询要 group</b>，换来的是一列一属性的可索引筛选。
/// </para>
/// <para>
/// <b>四列恰好一列（CK-5）</b>：<c>OptionId</c>/<c>NumberValue</c>/<c>TextValue</c>/<c>BoolValue</c>
/// 按属性的 <c>DataType</c> 择一写入。不写这条 CHECK 时，"两列都有值"会被不同读取路径
/// 各取一个 —— 两个真源，且都不报错。
/// </para>
/// <para>
/// <b><c>BoolValue</c> 的 CK-6（<c>IS NULL OR IN (0,1)</c>）不是多余的</b>：SQLite 的
/// <c>INTEGER</c> 接受 <c>2</c>，而 EF 的 <c>bool?</c> 映射是"非零即真" ——
/// 写入 <c>2</c> 后读回 <c>true</c>，全程无报错。<b>已实测</b>。
/// </para>
/// </remarks>
public sealed class ProductAttributeValueRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>Product</c>，<c>Cascade</c>（纯从属行）。</summary>
    public Guid ProductId { get; set; }

    /// <summary>FK → <c>AttributeDefinition</c>，<c>Restrict</c>：被引用为值的属性定义不得物理删除。</summary>
    public Guid AttributeDefinitionId { get; set; }

    /// <summary>枚举取值（<c>DataType = Enum/MultiEnum</c>）。</summary>
    public Guid? OptionId { get; set; }

    /// <summary>数值（<c>DataType = Number</c>），按 4 位小数四舍五入。</summary>
    public double? NumberValue { get; set; }

    /// <summary>文本（<c>DataType = Text/MultilineText</c>）。</summary>
    public string? TextValue { get; set; }

    /// <summary>布尔（<c>DataType = Bool</c>）。CK-6 限制为 0/1。</summary>
    public bool? BoolValue { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
