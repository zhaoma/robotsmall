using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductTranslation</c> 表映射。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一索引 <c>UX_ProductTranslation_Locale_Slug</c> 是全库最热的一条索引</b>
/// （数据模型 §4.1 第一条）：商品详情页按 URL 反查就靠它。它的成立前提是<b>写入前 slug 已统一小写</b>
/// ——SQLite 默认 <c>BINARY</c> 比较（<c>'A' &lt; 'a'</c>），PG 按 collation；若哪天有人在 PG 上用了
/// case-insensitive collation，<c>/products/Foo</c> 与 <c>/products/foo</c> 会撞键。
/// 所以 slug 规范化不是代码风格，是唯一性的前提。
/// </para>
/// </remarks>
public sealed class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslationRecord>
{
    public void Configure(EntityTypeBuilder<ProductTranslationRecord> builder)
    {
        builder.ToTable("ProductTranslation");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Locale)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Slug)
            .HasMaxLength(200)
            .IsRequired();

        // Description：富文本 HTML，不设 MaxLength（PG 上落到 text）
        builder.Property(t => t.ShortDescription).HasMaxLength(500);
        builder.Property(t => t.SeoTitle).HasMaxLength(200);
        builder.Property(t => t.SeoDescription).HasMaxLength(500);

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        // 纯从属行：商品物理删除时译文一并删（数据模型 §5-9 的 Cascade 白名单）。
        // 注意「物理删除」只在测试/重置环境发生；软删商品**不得删译文行**（slug 占位语义靠它承载）。
        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.ProductId, t.Locale })
            .IsUnique()
            .HasDatabaseName("UX_ProductTranslation_Product_Locale");

        builder.HasIndex(t => new { t.Locale, t.Slug })
            .IsUnique()
            .HasDatabaseName("UX_ProductTranslation_Locale_Slug");
    }
}
