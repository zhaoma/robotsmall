namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeDefinitionTranslation</c> 表：属性定义的 per-locale 名称与帮助文案。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么这张表 M1 必建（不是 M6 多语种批次才建）</b>（对照表 §1.0 补充项）：
/// 02 §3.1 定「筛选器标题就是 <c>Name</c>，必须翻译」，而 M1 的变体矩阵编辑器要把
/// 「颜色 / 白 / 尺码 / M」显示成双语。建表成本在单一迁移内为 0；
/// 留到 M6 就得额外做一次「把 label 回填进翻译行」的数据迁移。
/// </para>
/// <para>
/// 唯一 <c>(AttributeDefinitionId, Locale)</c>：一条属性在每个语言下只有一个名字。
/// 删除属性时译文 <c>Cascade</c> 清理（纯从属行，数据模型 §5-9 白名单）。
/// </para>
/// </remarks>
public sealed class AttributeDefinitionTranslationRecord
{
    public Guid Id { get; set; }

    public Guid AttributeDefinitionId { get; set; }

    /// <summary>BCP-47 语言标记（<c>zh-Hans</c> / <c>en</c>）。</summary>
    public string Locale { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? HelpText { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
