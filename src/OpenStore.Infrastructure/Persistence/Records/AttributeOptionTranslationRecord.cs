namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeOptionTranslation</c> 表：选项的 per-locale 显示名（<c>red</c> → 「白」/「White」）。
/// </summary>
/// <remarks>
/// <b><c>Label</c> 才是前台显示的文本，<c>AttributeOption.Code</c> 不是</b>。
/// 这条分工是「改显示名不影响变体」的实现基础（J7）：签名用 <c>Code</c>，展示用 <c>Label</c>。
/// 唯一 <c>(AttributeOptionId, Locale)</c>；删除选项时 <c>Cascade</c> 清理。
/// </remarks>
public sealed class AttributeOptionTranslationRecord
{
    public Guid Id { get; set; }

    public Guid AttributeOptionId { get; set; }

    public string Locale { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
