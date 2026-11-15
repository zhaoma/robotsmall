namespace OpenStore.Domain.Catalog;

/// <summary>
/// 商品状态。只有 <see cref="Active"/> 出现在 storefront（门店）侧；
/// <see cref="Draft"/> 是编辑中、<see cref="Archived"/> 已下架（旧 URL 返回 410 而非 301，见数据模型 §D4）。
/// </summary>
public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}
