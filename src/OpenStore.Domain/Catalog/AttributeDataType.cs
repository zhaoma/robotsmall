namespace OpenStore.Domain.Catalog;

/// <summary>
/// 属性定义的数据类型，决定值表里哪一列被写入（窄表四列恰好一列，CK-5）。
/// </summary>
/// <remarks>
/// <para>
/// 数值持久化在 <c>AttributeDefinition.DataType</c>（<c>INTEGER</c>）。<b>值只能追加，不得重排</b>。
/// </para>
/// <para>
/// 与 <c>ProductAttributeValue</c> 四列的对映关系：
/// <c>Enum</c> → <c>OptionId</c>；<c>MultiEnum</c> → 多行 <c>OptionId</c>；
/// <c>Number</c> → <c>NumberValue</c>；<c>Text</c>/<c>MultilineText</c> → <c>TextValue</c>；
/// <c>Bool</c> → <c>BoolValue</c>。任何"没有对应列"的组合都是数据错误，
/// 所以 CK-5（四列恰好一列）不是防御式编程，是把这条对映关系钉在库里。
/// </para>
/// </remarks>
public enum AttributeDataType
{
    /// <summary>单行文本。写入 <c>TextValue</c>。</summary>
    Text = 0,

    /// <summary>多行文本（富文本由前端消毒，库里仍是纯文本列）。写入 <c>TextValue</c>。</summary>
    MultilineText = 1,

    /// <summary>
    /// 单选项（如"颜色=白"）。写入 <c>OptionId</c>。
    /// <b>只有 Enum 型的属性才能作为变体轴</b>——矩阵要的是"有限取值集合"，
    /// 自由文本当轴会产生无穷多个签名。
    /// </summary>
    Enum = 2,

    /// <summary>多选项（如"适用场景=室内/户外"）。写成多行 <c>OptionId</c>。</summary>
    MultiEnum = 3,

    /// <summary>数值（写 <c>NumberValue</c>，按 4 位小数四舍五入）。可作为筛选区间。</summary>
    Number = 4,

    /// <summary>布尔（写 <c>BoolValue</c>）。CK-6 拦住 0/1 以外的脏值。</summary>
    Bool = 5,
}
