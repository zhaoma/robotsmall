using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>CategoryAttributeOverride</c> 表映射：<b>复合主键，无独立 Id</b>。
/// </summary>
/// <remarks>
/// 分类被物理删 → 覆盖行一并删（纯从属）；属性定义被删必须被拦。
/// <c>IsRequired</c> 是可空 bool，EF 会映射成可空 <c>INTEGER</c>，
/// <b>不要给它加 "IN (0,1)" 的 CHECK</b>：NULL 是合法且必要的一态（"跟随成员设置"），
/// 加了 CHECK 就必须写成 <c>IS NULL OR IN (0,1)</c>，而这里没有"非零即真"的解读分歧
/// （读取侧只有"空/真/假"三态，都由应用层显式判断），所以不值得多一条约束。
/// </remarks>
public sealed class CategoryAttributeOverrideConfiguration : IEntityTypeConfiguration<CategoryAttributeOverrideRecord>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeOverrideRecord> builder)
    {
        builder.ToTable("CategoryAttributeOverride");

        builder.HasKey(o => new { o.CategoryId, o.AttributeDefinitionId });
        builder.Property(o => o.CategoryId).ValueGeneratedNever();
        builder.Property(o => o.AttributeDefinitionId).ValueGeneratedNever();

        builder.Property(o => o.IsHidden).IsRequired();

        builder.HasOne<CategoryRecord>()
            .WithMany()
            .HasForeignKey(o => o.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(o => o.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.AttributeDefinitionId)
            .HasDatabaseName("IX_CategoryAttrOverride_Definition");
    }
}
