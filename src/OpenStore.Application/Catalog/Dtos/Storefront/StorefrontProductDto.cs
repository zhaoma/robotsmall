namespace OpenStore.Application.Catalog.Dtos.Storefront;

/// <summary>
/// 门店（storefront）商品详情。<b>这个类型是数据隔离的边界本身</b>：
/// 它由 <c>Category=StorefrontDto</c> 架构测试看守，属性名不得出现
/// Cost / Margin / InternalNote / OnHand / Reserved / Sold / ProviderTxn / Email / AdminId 等
/// 后台字段（后端设计 §1.1 硬禁止 #4）。
/// </summary>
/// <remarks>
/// 只暴露"单一展示价"：一个 Currency + AmountMinor。即使 M1 起变体支持多币种，
/// 前端仍只拿到"当前 locale 对应币种的那一个价"，多币种不体现为数组。
/// </remarks>
/// <param name="Slug">商品 slug（拼 canonical URL 用）。</param>
/// <param name="Title">商品标题（已按 locale 解析，缺失时回退默认语言）。</param>
/// <param name="Currency">展示价币种（ISO 4217）。</param>
/// <param name="AmountMinor">展示价，minor units。前端按币种小数位格式化。</param>
/// <param name="IsAvailable">
/// 是否可加购。M0 语义 = "存在正价格"；M1 接入库存后改为 <c>OnHand - Reserved &gt; 0</c>，
/// 且 ≤5 时截断真实值（后端设计 §5.3.1）。截断发生在映射层，不在这里。
/// </param>
public sealed record StorefrontProductDto(
    string Slug,
    string Title,
    string Currency,
    long AmountMinor,
    bool IsAvailable);
