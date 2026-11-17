namespace OpenStore.Domain.Catalog;

/// <summary>
/// 媒体资产的用途分类，决定"谁会引用它"。
/// </summary>
/// <remarks>
/// <para>
/// 数值持久化在 <c>MediaAsset.MediaType</c>（<c>INTEGER</c>）。<b>值只能追加</b>。
/// </para>
/// <para>
/// 之所以要分类型而不是一张裸文件表：商品图与主题图的生命周期不同 —— 换主题时会清理
/// <c>ThemeBrand</c>/<c>ThemeBlock</c> 的孤儿文件，但商品图必须保留（它挂在商品上）。
/// 没有这个区分，"清理未被引用的媒体"就会误删商品图。
/// </para>
/// </remarks>
public enum MediaType
{
    /// <summary>商品图（含变体图）。被 <c>ProductImage</c> 引用。</summary>
    Product = 0,

    /// <summary>主题品牌资源（logo、favicon）。被主题配置引用。</summary>
    ThemeBrand = 1,

    /// <summary>主题区块资源（banner 等）。被主题区块配置引用。</summary>
    ThemeBlock = 2,
}
