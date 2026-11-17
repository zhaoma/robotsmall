using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AttributeOptionTranslation</c> 表映射。</summary>
public sealed class AttributeOptionTranslationConfiguration : IEntityTypeConfiguration<AttributeOptionTranslationRecord>
{
    public void Configure(EntityTypeBuilder<AttributeOptionTranslationRecord> builder)
    {
        builder.ToTable("AttributeOptionTranslation");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Locale).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Label).HasMaxLength(200).IsRequired();

        builder.HasIndex(t => new { t.AttributeOptionId, t.Locale })
            .IsUnique()
            .HasDatabaseName("UX_AttrOptionTranslation_Option_Locale");

        builder.HasOne<AttributeOptionRecord>()
            .WithMany()
            .HasForeignKey(t => t.AttributeOptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
