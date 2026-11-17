namespace OpenStore.Domain.Catalog;

/// <summary>
/// 变体状态。<b>停用变体不进前台可选集，但保留历史销量与流水</b>（INV-P09）。
/// </summary>
/// <remarks>
/// 数值持久化在 <c>ProductVariant.Status</c>（<c>INTEGER</c>）。<b>枚举值一旦落库就不得重排</b>：
/// 重排等于让所有历史行换含义，且不会有任何报错——这正是"枚举值只能追加、不能插入"的原因。
/// </remarks>
public enum VariantStatus
{
    /// <summary>启用：可选、可下单。默认值 0。</summary>
    Enabled = 0,

    /// <summary>停用：保留历史，前台不可选、不可下单（有销量的变体只能停用，不能删）。</summary>
    Disabled = 1,
}
