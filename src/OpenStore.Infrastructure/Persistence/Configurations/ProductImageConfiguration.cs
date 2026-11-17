using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductImage</c> 表映射（商品/变体 ↔ 媒体资产绑定）。
/// </summary>
/// <remarks>
/// <b>本表与 <c>Product.PrimaryImageId</c> 构成循环外键</b>（对照表 §3.3）：
/// 保留循环、不做降级 —— SQLite 建表时不校验被引用表存在，PG 由 EF 生成器把其中一条
/// 后置成 <c>ALTER TABLE ADD CONSTRAINT</c>，两个 provider 都能生成。
/// 代价是插入顺序必须两段式（建 Product → 建 ProductImage → UPDATE Product），
/// 这是聚合持久化的正常形态，由 <c>ProductMapping</c> 负责。
/// </remarks>
public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImageRecord>
{
    public void Configure(EntityTypeBuilder<ProductImageRecord> builder)
    {
        builder.ToTable("ProductImage");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.MediaAssetId).IsRequired();
        builder.Property(i => i.SortOrder).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(255);

        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProductVariantRecord>()
            .WithMany()
            .HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // 被绑定的资产不得物理删除：删了图，绑定行就指向一个不存在的文件
        builder.HasOne<MediaAssetRecord>()
            .WithMany()
            .HasForeignKey(i => i.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // 商品图列表按 SortOrder 取，前导列必须是 ProductId
        builder.HasIndex(i => new { i.ProductId, i.SortOrder })
            .HasDatabaseName("IX_ProductImage_Product_Sort");

        builder.HasIndex(i => i.VariantId)
            .HasDatabaseName("IX_ProductImage_Variant");
    }
}
