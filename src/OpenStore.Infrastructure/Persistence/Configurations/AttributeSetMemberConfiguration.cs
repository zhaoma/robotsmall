using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>AttributeSetMember</c> 表映射：<b>复合主键，无独立 Id</b>。
/// </summary>
/// <remarks>
/// <b>必须显式 <c>ValueGeneratedNever()</c></b>（对照表 §1.2.10）：EF 默认会给
/// <c>Guid</c> 主键生成值，对复合主键的第一个属性也会尝试生成 —— 结果是插入时
/// <c>AttributeSetId</c> 被替换成一个新 Guid，成员关系挂到了不存在的集合上，且不报错。
/// </remarks>
public sealed class AttributeSetMemberConfiguration : IEntityTypeConfiguration<AttributeSetMemberRecord>
{
    public void Configure(EntityTypeBuilder<AttributeSetMemberRecord> builder)
    {
        builder.ToTable("AttributeSetMember");

        builder.HasKey(m => new { m.AttributeSetId, m.AttributeDefinitionId });
        builder.Property(m => m.AttributeSetId).ValueGeneratedNever();
        builder.Property(m => m.AttributeDefinitionId).ValueGeneratedNever();

        builder.Property(m => m.IsRequired).IsRequired();
        builder.Property(m => m.SortOrder).IsRequired();

        // 集合被删 → 成员关系一并删（纯从属）；属性定义被删必须被拦（Restrict）：成员关系是它的引用
        builder.HasOne<AttributeSetRecord>()
            .WithMany()
            .HasForeignKey(m => m.AttributeSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(m => m.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // 反向查"这个属性被哪些集合包含"（属性删除前的影响面检查）
        builder.HasIndex(m => m.AttributeDefinitionId)
            .HasDatabaseName("IX_AttributeSetMember_Definition");
    }
}
