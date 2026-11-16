using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>CategoryTranslation</c> 表映射。
/// </summary>
/// <remarks>
/// <b>两条唯一索引都不过滤软删</b>（数据模型 §4.3.5 全局策略）：软删分类的 slug 继续占位，
/// 防止新分类复用旧 slug 让旧外链 301 到不相干页面。过滤软删的唯一索引会把"撞号"这个
/// <b>响亮的错误</b>变成<b>静默的数据重号</b>。
/// </remarks>
public sealed class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslationRecord>
{
    public void Configure(EntityTypeBuilder<CategoryTranslationRecord> builder)
    {
        builder.ToTable("CategoryTranslation");

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

        // Description 不设 MaxLength：PG 上落到 text（数据模型 §1.1 T7 的 J 类例外同款处理）
        builder.Property(t => t.SeoTitle).HasMaxLength(200);
        builder.Property(t => t.SeoDescription).HasMaxLength(500);

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        // 纯从属行：分类物理删除时译文一并删（数据模型 §5-9 的 Cascade 白名单）
        builder.HasOne<CategoryRecord>()
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.CategoryId, t.Locale })
            .IsUnique()
            .HasDatabaseName("UX_CategoryTranslation_Category_Locale");

        // 分类页按 URL 反查的唯一点查路径
        builder.HasIndex(t => new { t.Locale, t.Slug })
            .IsUnique()
            .HasDatabaseName("UX_CategoryTranslation_Locale_Slug");
    }
}
