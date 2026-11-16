using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductCategory</c> 表映射：复合主键 + <b>部分唯一索引</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>部分唯一索引 <c>UX_ProductCategory_PrimaryPerProduct</c> 为什么必须有</b>（数据模型 §4.1）：
/// 面包屑与 canonical 路径依赖"主分类"，两个 primary ⇒ <b>两个都合法的答案</b>；而歧义不会报错，
/// 只会在某次查询计划变化时换一个——这正是本文反复在治的"两个真源"。
/// 它拦不住"一个都没有"，那一半由领域/装载校验保证
/// （<see cref="Domain.Catalog.ProductCategory.EnsureExactlyOnePrimaryPerProduct"/>）。
/// </para>
/// <para>
/// <b>这是 M1 唯一需要 provider 分支的 filter</b>（数据模型 §5-13 白名单）：SQLite 是布尔裸写
/// 真值判断（<c>WHERE "IsPrimary" = 1</c>），PG 是 <c>WHERE "IsPrimary"</c>（PG 上
/// <c>boolean = integer</c> 直接报错）。当前仓库只接了 SQLite provider（数据模型 §6.2 要求
/// 双 provider 两套独立迁移目录），因此本文件只声明 SQLite 形态，并把 PG 形态以常量形式留在旁边
/// ——**接 PG 目录时只需把 <see cref="SqlitePrimaryFilter"/> 换成 <see cref="PostgresPrimaryFilter"/>，
/// 不要在两处各写一份**。
/// </para>
/// </remarks>
public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategoryRecord>
{
    /// <summary>主分类的部分唯一索引名（数据模型 §3.1 / §4.1）。</summary>
    public const string PrimaryPerProductIndex = "UX_ProductCategory_PrimaryPerProduct";

    /// <summary>SQLite 的 filter 形态（当前生效）。</summary>
    public const string SqlitePrimaryFilter = "\"IsPrimary\" = 1";

    /// <summary>PostgreSQL 的 filter 形态（接 PG provider 时切换到这里）。</summary>
    public const string PostgresPrimaryFilter = "\"IsPrimary\"";

    public void Configure(EntityTypeBuilder<ProductCategoryRecord> builder)
    {
        // CK-9（M1 裁决新增，见对照表 §4.1）：IsPrimary 的"唯一真源"问题。
        // IsPrimary 落 SQLite 是 INTEGER，而两个消费者对它的解读范围**不同**：
        //   部分唯一索引 `… WHERE "IsPrimary" = 1` → 看不见 2（认为"这个商品没有主分类"）；
        //   EF 的 bool 映射 → 非零即真（认为"它是主分类"）。
        // 于是一行 IsPrimary=2 可以同时造成"库认为没有主分类"与"应用认为有主分类"，
        // 且**不报任何错** —— 面包屑与 canonical 会在某次查询计划变化时换一个答案。
        // 不写这条 CHECK 就只能靠"没人会写 2"来保证，那是把不变量交给运气。
        builder.ToTable("ProductCategory", t =>
            t.HasCheckConstraint("CK_ProductCategory_IsPrimary_Bit", "\"IsPrimary\" IN (0, 1)"));

        // 复合主键，无独立 Id：多对多绑定的身份就是这两列
        builder.HasKey(pc => new { pc.ProductId, pc.CategoryId });

        builder.Property(pc => pc.IsPrimary).IsRequired();
        builder.Property(pc => pc.SortOrder).IsRequired();
        builder.Property(pc => pc.CreatedAt).IsRequired();
        builder.Property(pc => pc.UpdatedAt).IsRequired();

        // 物理删商品时绑定行一并删（只在测试/重置路径发生；软删清空绑定是禁止的，见 ProductCategoryRecord 注释）
        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // 有商品归属的分类不得物理删除（数据模型 §1.3 "条件硬删"的 DB 侧兜底）
        builder.HasOne<CategoryRecord>()
            .WithMany()
            .HasForeignKey(pc => pc.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // 分类页主查询：按分类过滤商品
        builder.HasIndex(pc => new { pc.CategoryId, pc.ProductId })
            .HasDatabaseName("IX_ProductCategory_Category_Product");

        // 详情页面包屑 / 多分类并集：`WHERE "ProductId" = ?`（取该商品全部绑定，不只看主分类）。
        //
        // 注意：数据模型 §4.1 另列了一条 `IX_ProductCategory_Product (ProductId)`，本文件**不建**它——
        // 复合主键 `(ProductId, CategoryId)` 的索引前导列就是 ProductId，`WHERE "ProductId" = ?`
        // 已经在用 PK 索引；而 EF 把"同一列集合上的索引"视为同一条（再调一次 HasIndex 只会改名字，
        // 不会多建），所以下面这条**带 filter 的唯一索引**才是 ProductId 上唯一可建的第二条。
        // 换句话说：§4.1 的 `IX_ProductCategory_Product` 与 `PK_ProductCategory` 重复，属文档冗余项。
        // 它拦不住"一个都没有"，那一半由领域/装载校验保证（见 ProductCategory 领域类）。

        // 每个商品至多一个主分类。filter 表达式见类型注释（M1 唯一的 provider 分支点）。
        builder.HasIndex(pc => pc.ProductId)
            .IsUnique()
            .HasFilter(SqlitePrimaryFilter)
            .HasDatabaseName(PrimaryPerProductIndex);
    }
}
