using Microsoft.EntityFrameworkCore;

namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>Product</c> 表的持久化模型（与领域聚合 <see cref="Domain.Catalog.Product"/> 分离）。
/// </summary>
/// <remarks>
/// <b>为什么不直接把领域对象交给 EF</b>：领域层零第三方依赖是硬约束，
/// 而 EF 的物化要求"无参构造 + 可写导航属性"，会逼着我们在 <c>Product</c> 里加
/// <c>private Product() { }</c>、把不变量校验拆成两步——领域模型就被 ORM 牵着走了。
/// 用独立的 Record 承担"数据库长什么样"，领域模型承担"业务规则是什么"，
/// 二者的差异在 Repository 里收敛成一处显式映射。
/// </remarks>
public sealed class ProductRecord
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>存 <see cref="Domain.Catalog.ProductStatus"/> 的数值（不用字符串：改枚举名不该触发数据迁移）。</summary>
    public int Status { get; set; }

    public List<ProductVariantRecord> Variants { get; set; } = new();
}
