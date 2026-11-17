namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>VariantAttributeValue</c> 表：<b>变体级</b>属性值，即"这个变体在每根轴上的取值"。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="OptionId"/> 必须 <c>NOT NULL</c></b>（数据模型 §1.2.12）：
/// 变体轴只允许枚举选项（<c>VariantMatrix</c> 保证），
/// 可空会让"某根轴没有取值"变成一种合法的数据 —— 而那种变体在签名里会出现空位
/// （<c>color=|size=m</c>），前台选择器无法处理。
/// </para>
/// <para>
/// <b>唯一 <c>(VariantId, AttributeDefinitionId)</c> = 「每轴恰好一行」</b>：
/// 一个变体在同一根轴上不能既红又蓝。这条唯一约束比领域校验更硬，
/// 因为批量导入可能绕过领域。
/// </para>
/// <para>
/// <b>索引 <c>(AttributeDefinitionId, OptionId)</c> 服务的是"按 颜色=白 批量定位变体"</b> ——
/// 前台筛选一个颜色时要拿到所有匹配变体，这条索引的前导列必须是属性定义而不是变体。
/// </para>
/// </remarks>
public sealed class VariantAttributeValueRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>ProductVariant</c>，<c>Cascade</c>（变体的纯从属行）。</summary>
    public Guid VariantId { get; set; }

    /// <summary>FK → <c>AttributeDefinition</c>，<c>Restrict</c>。</summary>
    public Guid AttributeDefinitionId { get; set; }

    /// <summary>该轴取值，<c>NOT NULL</c>，FK → <c>AttributeOption</c> <c>Restrict</c>。</summary>
    public Guid OptionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
