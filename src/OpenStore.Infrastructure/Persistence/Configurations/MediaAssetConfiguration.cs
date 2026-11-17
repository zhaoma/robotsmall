using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>MediaAsset</c> 表映射（不可变表，无 <c>UpdatedAt</c>）。</summary>
public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAssetRecord>
{
    public void Configure(EntityTypeBuilder<MediaAssetRecord> builder)
    {
        builder.ToTable("MediaAsset");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(m => m.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(m => m.MimeType).HasMaxLength(64).IsRequired();
        builder.Property(m => m.ByteSize).IsRequired();
        builder.Property(m => m.Width).IsRequired();
        builder.Property(m => m.Height).IsRequired();
        builder.Property(m => m.MediaType).IsRequired();
        builder.Property(m => m.OriginalName).HasMaxLength(255);

        // StorageKey 唯一：重复写入同一对象存储路径会静默覆盖别人的图，唯一索引把这个错误变成插入失败
        builder.HasIndex(m => m.StorageKey)
            .IsUnique()
            .HasDatabaseName("UX_MediaAsset_StorageKey");

        // ContentHash 普通索引：去重/秒传的查找加速，不是身份标识（不同文件 hash 相同的前提不成立）
        builder.HasIndex(m => m.ContentHash)
            .HasDatabaseName("IX_MediaAsset_ContentHash");
    }
}
