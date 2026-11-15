using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductVariant</c> 表映射。
/// </summary>
/// <remarks>
/// 唯一索引 <c>(ProductId, AxisSignature)</c> 是"变体矩阵不重复"的<b>最后一道</b>防线：
/// 领域层先拦（好报错），DB 再拦（防并发/绕过用例的直接写入）。
/// </remarks>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariantRecord>
{
    public void Configure(EntityTypeBuilder<ProductVariantRecord> builder)
    {
        builder.ToTable("ProductVariant");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Sku)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(v => v.AxisSignature)
            .HasMaxLength(400)
            .IsRequired();

        // 币种固定 3 位（ISO 4217），不用枚举整数：改枚举会污染所有历史数据
        builder.Property(v => v.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(v => v.AmountMinor)
            .IsRequired();

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.ProductId, v.AxisSignature })
            .IsUnique()
            .HasDatabaseName("IX_ProductVariant_Product_AxisSignature");

        builder.HasIndex(v => v.Sku)
            .IsUnique()
            .HasDatabaseName("IX_ProductVariant_Sku");
    }
}
