using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AttributeGroup</c> 表映射（显式，无约定推断）。</summary>
public sealed class AttributeGroupConfiguration : IEntityTypeConfiguration<AttributeGroupRecord>
{
    public void Configure(EntityTypeBuilder<AttributeGroupRecord> builder)
    {
        builder.ToTable("AttributeGroup");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Code).HasMaxLength(64).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.SortOrder).IsRequired();

        // Code 唯一不过滤软删：本表无软删列（分组删了就是删了，属性上的 GroupId 是 Restrict 会拦住）
        builder.HasIndex(g => g.Code)
            .IsUnique()
            .HasDatabaseName("UX_AttributeGroup_Code");
    }
}
