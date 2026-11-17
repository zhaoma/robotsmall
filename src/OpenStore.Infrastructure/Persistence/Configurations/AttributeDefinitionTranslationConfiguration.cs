using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AttributeDefinitionTranslation</c> 表映射。</summary>
public sealed class AttributeDefinitionTranslationConfiguration : IEntityTypeConfiguration<AttributeDefinitionTranslationRecord>
{
    public void Configure(EntityTypeBuilder<AttributeDefinitionTranslationRecord> builder)
    {
        builder.ToTable("AttributeDefinitionTranslation");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Locale).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.HelpText).HasMaxLength(500);

        // 一条属性在每个语言下只有一个名字：重复行会让"筛选器标题"取决于查询顺序
        builder.HasIndex(t => new { t.AttributeDefinitionId, t.Locale })
            .IsUnique()
            .HasDatabaseName("UX_AttrDefTranslation_Def_Locale");

        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(t => t.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
