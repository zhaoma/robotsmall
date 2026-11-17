using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductVariantAxis</c> 表映射（商品的变体轴定义）。
/// </summary>
/// <remarks>
/// <b>CK-8 强制 <c>SortOrder</c> 落在 1..3</b>：它决定签名里的键排序
/// （<c>color=red|size=m</c> 的先后由轴顺序定，签名内部按键名 Ordinal 排序则是另一层）。
/// 脏值（0 或 9）不会报错，只会让选择器顺序在两次查询间变化。
/// 「轴数 ≤3」仍是领域不变量，CK-8 不替代它。
/// </remarks>
public sealed class ProductVariantAxisConfiguration : IEntityTypeConfiguration<ProductVariantAxisRecord>
{
    /// <summary>CK-8 名称（§4.1 按名对账，不得改名）。</summary>
    public const string SortOrderCheckName = "CK_ProductVariantAxis_SortOrder";

    public void Configure(EntityTypeBuilder<ProductVariantAxisRecord> builder)
    {
        builder.ToTable("ProductVariantAxis", t =>
            t.HasCheckConstraint(SortOrderCheckName, "\"SortOrder\" BETWEEN 1 AND 3"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ProductId).IsRequired();
        builder.Property(a => a.AttributeDefinitionId).IsRequired();
        builder.Property(a => a.SortOrder).IsRequired();

        // 轴定义是商品的纯从属行，无外部引用 → Cascade
        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // 被当轴用的属性定义不得物理删除
        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(a => a.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // 同一商品不能把同一个属性定义挂两次（否则同一根轴出现两行，签名会重复一个维度）
        builder.HasIndex(a => new { a.ProductId, a.AttributeDefinitionId })
            .IsUnique()
            .HasDatabaseName("UX_VariantAxis_Product_Def");
    }
}
