namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeOption</c> 表：枚举型属性的一个可选值（如「颜色」下的 <c>red</c> / <c>white</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Code"/> 是变体轴签名的组成部分</b>（<c>color=red</c>），所以它在
/// <c>(AttributeDefinitionId, Code)</c> 上唯一，且<b>改 <c>Code</c> 会让已生成的变体签名与轴值对不上</b>
/// —— 领域侧把 <c>Code</c> 视为创建后不可改（可改的是译文 <c>Label</c>）。
/// 这条设计让「把 red 的显示名改成 crimson」不影响任何变体（J7 裁决）。
/// </para>
/// <para>
/// <b>本表没有 <c>IsDeleted</c>，只有 <see cref="IsActive"/></b>：被值表或变体轴引用时不允许删除
/// （条件硬删由领域/外键 <c>Restrict</c> 保证），"不想让它出现"用停用表达。
/// <c>IsActive</c> 不加 CHECK、不加索引：选项总数是百级，扫一遍的代价可以忽略。
/// </para>
/// </remarks>
public sealed class AttributeOptionRecord
{
    public Guid Id { get; set; }

    /// <summary>FK → <c>AttributeDefinition</c>，<c>Restrict</c>：有选项的属性定义不得物理删除。</summary>
    public Guid AttributeDefinitionId { get; set; }

    /// <summary>稳定业务码，进入变体轴签名；在所属属性定义内唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>色板色值（如 <c>#FFFFFF</c>），仅 <c>#RGB</c>/<c>#RRGGBB</c>/<c>#RRGGBBAA</c> 有意义，前端直接画色块。</summary>
    public string? ColorHex { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
