using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>Product</c> 表映射（显式，无约定推断）。
/// </summary>
/// <remarks>
/// <b>slug 唯一索引不附加软删过滤</b>（数据模型 §D4）：软删商品的 slug 继续占位，
/// 防止新品复用旧 slug 导致旧外链跳到不相干商品。
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

        // 商品标题是默认语言的回退值，真实标题在 ProductTranslation（M1）
        builder.Property(p => p.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired();

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Product_Slug");
    }
}
