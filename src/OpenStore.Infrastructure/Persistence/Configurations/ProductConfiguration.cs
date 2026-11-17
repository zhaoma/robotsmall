using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>Product</c> 表映射（显式，无约定推断）。
/// </summary>
/// <remarks>
/// <para>
/// <b>slug 唯一索引不附加软删过滤</b>（数据模型 §D4）：软删商品的 slug 继续占位，
/// 防止新品复用旧 slug 导致旧外链跳到不相干商品。
/// </para>
/// <para>
/// <b>与 <c>ProductImage</c> 的循环外键</b>（对照表 §3.3）：<see cref="ProductRecord.PrimaryImageId"/>
/// 指向 <c>ProductImage.Id</c>，而 <c>ProductImage.ProductId</c> 又指回来。
/// 保留循环、不做降级 —— SQLite 建表时不校验被引用表存在，PG 由 EF 生成器后置一条
/// <c>ALTER TABLE ADD CONSTRAINT</c>，两个 provider 都能生成。
/// 代价是实体插入必须两段式（Product → ProductImage → UPDATE Product.PrimaryImageId），
/// 由 <c>ProductMapping</c> 负责；这个顺序不是缺陷，是聚合持久化的正常形态。
/// </para>
/// </remarks>
public sealed class ProductConfiguration : IEntityTypeConfiguration<ProductRecord>
{
    public void Configure(EntityTypeBuilder<ProductRecord> builder)
    {
        builder.ToTable("Product");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug)
            .HasMaxLength(200)
            .IsRequired();

        // 商品标题是默认语言的回退值，真实标题在 ProductTranslation（迁移顺序见 ProductRecord 注释）
        builder.Property(p => p.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(p => p.InternalCode).HasMaxLength(64);
        builder.Property(p => p.WeightGram);

        builder.Property(p => p.Status)
            .IsRequired();
        builder.Property(p => p.MinPriceMinor).IsRequired();
        builder.Property(p => p.MaxPriceMinor).IsRequired();
        builder.Property(p => p.IsDeleted).IsRequired();

        // 并发令牌：两个管理员同时改同一商品时，后写者失败而不是静默覆盖价格/状态
        builder.Property(p => p.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Product_Slug");

        // 部分唯一索引：内部编码"可空但唯一"。多数商品没有内部编码，
        // 若用普通唯一索引，第二行 NULL 也会撞（SQLite 与 PG 对多 NULL 的处理还不一致）
        builder.HasIndex(p => p.InternalCode)
            .IsUnique()
            .HasFilter("\"InternalCode\" IS NOT NULL")
            .HasDatabaseName("UX_Product_InternalCode");

        // 一张图不能被两个商品同时当主图
        builder.HasIndex(p => p.PrimaryImageId)
            .IsUnique()
            .HasDatabaseName("UX_Product_PrimaryImageId");

        // 列表页默认视图：按状态过滤 + 按上架时间倒序
        builder.HasIndex(p => new { p.Status, p.PublishedAt })
            .HasDatabaseName("IX_Product_Status_PublishedAt");

        // 列表页按价格排序（投影列，见 ProductRecord.MinPriceMinor）
        builder.HasIndex(p => new { p.Status, p.MinPriceMinor, p.Id })
            .HasDatabaseName("IX_Product_Status_MinPrice");

        // 主图指针 → ProductImage，Restrict：删图会让本列悬空
        builder.HasOne<ProductImageRecord>()
            .WithMany()
            .HasForeignKey(p => p.PrimaryImageId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
