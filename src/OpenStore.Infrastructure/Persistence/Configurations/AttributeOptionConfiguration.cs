using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>AttributeOption</c> 表映射。</summary>
/// <remarks>
/// <b>唯一键是 <c>(AttributeDefinitionId, Code)</c> 而不是 <c>Code</c> 单列</b>：
/// 不同属性可以有同名选项（「颜色=white」与「背光=white」是两件事），
/// 但同一属性内不能重复 —— 重复会让签名 <c>color=white</c> 指向两个选项。
/// </remarks>
public sealed class AttributeOptionConfiguration : IEntityTypeConfiguration<AttributeOptionRecord>
{
    public void Configure(EntityTypeBuilder<AttributeOptionRecord> builder)
    {
        builder.ToTable("AttributeOption");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Code).HasMaxLength(64).IsRequired();
        builder.Property(o => o.SortOrder).IsRequired();
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.ColorHex).HasMaxLength(9);

        builder.HasIndex(o => new { o.AttributeDefinitionId, o.Code })
            .IsUnique()
            .HasDatabaseName("UX_AttributeOption_Def_Code");

        // Restrict：被值表/变体轴引用的选项不得物理删除（条件硬删的 DB 侧兜底）
        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(o => o.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
