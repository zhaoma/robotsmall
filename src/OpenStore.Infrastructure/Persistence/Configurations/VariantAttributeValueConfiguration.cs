using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary><c>VariantAttributeValue</c> 表映射（变体在每根轴上的取值）。</summary>
/// <remarks>
/// <b>唯一 <c>(VariantId, AttributeDefinitionId)</c> = 每轴恰好一行</b>：
/// 一个变体不能在同一根轴上既有"红"又有"蓝"。这条比领域校验更硬，
/// 因为批量导入/直接写库会绕过领域。
/// </remarks>
public sealed class VariantAttributeValueConfiguration : IEntityTypeConfiguration<VariantAttributeValueRecord>
{
    public void Configure(EntityTypeBuilder<VariantAttributeValueRecord> builder)
    {
        builder.ToTable("VariantAttributeValue");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VariantId).IsRequired();
        builder.Property(v => v.AttributeDefinitionId).IsRequired();
        builder.Property(v => v.OptionId).IsRequired();

        builder.HasOne<ProductVariantRecord>()
            .WithMany()
            .HasForeignKey(v => v.VariantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(v => v.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AttributeOptionRecord>()
            .WithMany()
            .HasForeignKey(v => v.OptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.VariantId, v.AttributeDefinitionId })
            .IsUnique()
            .HasDatabaseName("UX_VAV_Variant_Attr");

        // "按 颜色=白 批量定位变体"：前导列必须是属性定义而不是变体
        builder.HasIndex(v => new { v.AttributeDefinitionId, v.OptionId })
            .HasDatabaseName("IX_VAV_Attr_Option");
    }
}
