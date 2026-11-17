namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AttributeGroup</c> 表：属性分组（如「外观」「规格」）。
/// </summary>
/// <remarks>
/// <b><see cref="Name"/> 只用于管理端展示，刻意不建翻译表</b>（数据模型 §1.2.7 / 02 裁决 1）：
/// 分组是管理员自己看的组织工具，前台永远不显示它；为它建翻译表只会多出两张永远只有一行的表。
/// 需要双语的是<b>属性定义</b>与<b>选项</b>（它们出现在前台筛选器上），不是分组。
/// </remarks>
public sealed class AttributeGroupRecord
{
    public Guid Id { get; set; }

    /// <summary>稳定业务码（模板幂等 upsert 用），唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
