using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>AttributeDefinition</c> 表映射（属性体系聚合根）。
/// </summary>
/// <remarks>
/// <b>两条索引服务两种不同的查询</b>（数据模型 §1.2.8）：
/// <c>IX_AttrDef_Scope</c> 服务"列出所有变体级属性"（矩阵编辑器选轴时的候选集）；
/// <c>IX_AttrDef_Filterable_Scope</c> 服务"列出所有可筛选的变体级属性"（前台筛选器组装）。
/// 后者把 <c>IsFilterable</c> 放前导列，因为它通常还要带 <c>IsActive = 1</c> 一起过滤。
/// </remarks>
public sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<AttributeDefinitionRecord> builder)
    {
        builder.ToTable("AttributeDefinition");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(64).IsRequired();
        builder.Property(d => d.DataType).IsRequired();
        builder.Property(d => d.Scope).IsRequired();
        builder.Property(d => d.IsFilterable).IsRequired();
        builder.Property(d => d.Unit).HasMaxLength(16);
        builder.Property(d => d.SortOrder).IsRequired();
        builder.Property(d => d.IsActive).IsRequired();
        builder.Property(d => d.IsDeleted).IsRequired();

        // 软删的属性定义继续占住 Code（与 SKU 同策略）：新建同名 Code 会与历史译文/值表语义重叠
        builder.HasIndex(d => d.Code)
            .IsUnique()
            .HasDatabaseName("UX_AttributeDefinition_Code");

        builder.HasIndex(d => d.Scope)
            .HasDatabaseName("IX_AttrDef_Scope");

        builder.HasIndex(d => new { d.IsFilterable, d.Scope })
            .HasDatabaseName("IX_AttrDef_Filterable_Scope");

        // 分组删除受 Restrict 保护：还有属性归属于它时不得物理删除
        builder.HasOne<AttributeGroupRecord>()
            .WithMany()
            .HasForeignKey(d => d.GroupId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
