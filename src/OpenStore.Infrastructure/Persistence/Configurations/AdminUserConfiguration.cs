using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AdminUser</c> 表映射（显式，无约定推断）。</summary>
public sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUserRecord>
{
    public void Configure(EntityTypeBuilder<AdminUserRecord> builder)
    {
        builder.ToTable("AdminUser");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.EmailNormalized)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.Role).IsRequired();
        builder.Property(u => u.Status).IsRequired();
        builder.Property(u => u.FailedLoginCount).IsRequired();
        builder.Property(u => u.Version).IsRequired();

        builder.HasIndex(u => u.EmailNormalized)
            .IsUnique()
            .HasDatabaseName("UX_AdminUser_EmailNormalized");
    }
}
