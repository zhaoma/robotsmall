namespace OpenStore.Domain.Catalog;

/// <summary>
/// 属性的作用域：商品级还是变体级。<b>它决定这颗属性出现在商品编辑器的哪个区块</b>。
/// </summary>
/// <remarks>
/// <para>
/// 数值持久化在 <c>AttributeDefinition.Scope</c>（<c>INTEGER</c>），并参与
/// <c>IX_AttrDef_Scope</c> 与 <c>IX_AttrDef_Filterable_Scope</c> 两条索引。
/// </para>
/// <para>
/// <b>为什么不用「一个属性两处都能填」</b>：那会让"这个商品是棉的还是某个变体是棉的"出现两个答案，
/// 而两个答案都会在筛选器里被当成事实。作用域必须唯一。
/// </para>
/// </remarks>
public enum AttributeScope
{
    /// <summary>商品级：整件商品共享一个值（如"材质=棉"）。</summary>
    Product = 0,

    /// <summary>变体级：每个变体各有取值（如"颜色"、"尺码"）。变体轴只能来自本作用域。</summary>
    Variant = 1,
}
