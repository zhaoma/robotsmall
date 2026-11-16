namespace OpenStore.Domain.Catalog;

/// <summary>
/// 分类可见状态（数据模型 §3.1 的 <c>Status | E | Active / Hidden</c>）。
/// </summary>
/// <remarks>
/// <b>数值是永久契约，禁止重编号</b>（数据模型 §6.5「重命名枚举值禁止改数值」）：
/// 改数值会让所有历史行静默换义，而这类漂移没有任何约束能发现。
/// <see cref="Hidden"/> 不等于删除：隐藏分类仍保留其商品归属（归档/隐藏都不清
/// <c>ProductCategory</c>，见 §3.1「归档不改变这两条」），只是不出现在导航树与分类页。
/// </remarks>
public enum CategoryStatus
{
    Active = 0,
    Hidden = 1,
}
