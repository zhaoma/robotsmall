using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AttributeSet</c> 表映射（显式，无约定推断）。</summary>
public sealed class AttributeSetConfiguration : IEntityTypeConfiguration<AttributeSetRecord>
{
    public void Configure(EntityTypeBuilder<AttributeSetRecord> builder)
    {
        builder.ToTable("AttributeSet");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SortOrder).IsRequired();

        builder.HasIndex(s => s.Code)
            .IsUnique()
            .HasDatabaseName("UX_AttributeSet_Code");
    }
}
