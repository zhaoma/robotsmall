using Microsoft.EntityFrameworkCore;

namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>Product</c> 表的持久化模型（与领域聚合 <see cref="Domain.Catalog.Product"/> 分离）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不直接把领域对象交给 EF</b>：领域层零第三方依赖是硬约束，
/// 而 EF 的物化要求"无参构造 + 可写导航属性"，会逼着我们在 <c>Product</c> 里加
/// <c>private Product() { }</c>、把不变量校验拆成两步——领域模型就被 ORM 牵着走了。
/// 用独立的 Record 承担"数据库长什么样"，领域模型承担"业务规则是什么"，
/// 二者的差异在 Repository 里收敛成一处显式映射。
/// </para>
/// <para>
/// <b><see cref="Slug"/> / <see cref="Title"/> 是 M0 遗留列，本轮<b>不搬</b>到
/// <c>ProductTranslation</c></b>：<see cref="ProductTranslationRecord"/> 的注释给了完整理由
/// —— 数据模型 §6.6 的不可逆顺序要求「先回填 <c>ProductCategory</c> 并校验通过 → 再迁 slug
/// → 再切读路径 → 跨 ≥1 个版本之后才允许 drop <c>Product.Slug</c>」。
/// 本轮处于第 1 步之前，所以两套结构<b>并存</b>，读路径仍走本表。
/// 这属于"已登记的双写过渡态"，不是两个真源：写入方只有 Admin 商品端点一处，
/// 翻译行由同一事务一并写（见 C2/C3 的裁决）。
/// </para>
/// </remarks>
public sealed class ProductRecord
{
    public Guid Id { get; set; }

    /// <summary>M0 遗留列，迁往 <c>ProductTranslation.Slug</c> 前继续作为唯一读路径。</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>M0 遗留列，迁往 <c>ProductTranslation.Name</c> 前继续作为默认语言标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内部编码（SKU 前缀/ERP 编号）。<b>可空但唯一</b>：部分唯一索引只对有值的行生效，
    /// 所以"大部分商品没有内部编码"与"两个商品的内部编码不能撞"可以同时成立。
    /// </summary>
    public string? InternalCode { get; set; }

    /// <summary>存 <see cref="Domain.Catalog.ProductStatus"/> 的数值（不用字符串：改枚举名不该触发数据迁移）。</summary>
    public int Status { get; set; }

    /// <summary>首次转 <c>Active</c> 时写一次（<c>PublishedAt ??= now</c>）；回归 Draft/Archived <b>不改写</b>。</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// <b>投影列</b>：由 <c>Pricing</c> 域服务在「变体改价 / 变体启停 / 变体增删 / 变体软删」
    /// 的同一事务内重算。唯一写入口是那一处；列表页按价格排序直接用它，避免每次 join 变体求 MIN。
    /// </summary>
    public long MinPriceMinor { get; set; }

    /// <summary>投影列，与 <see cref="MinPriceMinor"/> 同一次重算。</summary>
    public long MaxPriceMinor { get; set; }

    /// <summary>
    /// 主图指针。FK → <c>ProductImage</c>，<c>Restrict</c>（删图会让本列悬空）。
    /// <b>与 <c>ProductImage.ProductId</c> 构成循环外键</b>（对照表 §3.3）：保留，不降级。
    /// 「恰好 1 张主图」由聚合保证；DB 只保证"一张图不能被两个商品同时当主图"（唯一索引）。
    /// </summary>
    public Guid? PrimaryImageId { get; set; }

    /// <summary>商品级重量（克）；变体可覆盖。</summary>
    public int? WeightGram { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁版本号（统一 <c>long</c>，不用 <c>rowversion</c>/<c>xmin</c>：后者 provider 专有）。
    /// 由 <c>AuditFieldsInterceptor</c> 维护：<c>Added</c> 置 1，<c>Modified</c> 自增。
    /// </summary>
    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ProductVariantRecord> Variants { get; set; } = new();
}
