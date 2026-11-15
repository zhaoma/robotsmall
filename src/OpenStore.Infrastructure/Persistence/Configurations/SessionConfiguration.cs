using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>Session</c> 表映射（显式，无约定推断）。</summary>
public sealed class SessionConfiguration : IEntityTypeConfiguration<SessionRecord>
{
    public void Configure(EntityTypeBuilder<SessionRecord> builder)
    {
        builder.ToTable("Session");
        builder.HasKey(s => s.TokenHash);

        builder.Property(s => s.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.ActorType).IsRequired();
        builder.Property(s => s.ActorId).IsRequired();

        builder.HasIndex(s => new { s.ActorType, s.ActorId })
            .HasDatabaseName("IX_Session_Actor");

        builder.HasIndex(s => s.AbsoluteExpiresAt)
            .HasDatabaseName("IX_Session_AbsoluteExpiresAt");
    }
}
