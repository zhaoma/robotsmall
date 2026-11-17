using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductAttributeValue</c> 表映射：窄值表 + 两条互斥 CHECK。
/// </summary>
/// <remarks>
/// <para>
/// <b>CK-5 是本表的灵魂</b>：四列恰好一列有值。没有它，"两列都有值"会让不同读取路径
/// 各取一个（列表页读 <c>TextValue</c>、筛选读 <c>NumberValue</c>），两个都是"真的"，
/// 且不会报错。<b>CHECK 是唯一能同时约束所有写入方的地方</b>——领域校验会被批量导入绕过。
/// </para>
/// <para>
/// <b>CK-6 不是防御式编程</b>：SQLite 的 <c>INTEGER</c> 接受 <c>2</c>，EF 的 <c>bool?</c> 映射是
/// "非零即真"，所以写入 <c>2</c> 后读回 <c>true</c>、全程无报错（已实测）。
/// </para>
/// <para>
/// <b>索引分工</b>：<c>UX_PAV_Product_Def_Option</c> 是部分唯一索引（<c>WHERE OptionId IS NOT NULL</c>），
/// 保证"同一商品同一属性不重复选同一个选项"；<c>IX_PAV_Facet</c> 是筛选器的主路径
/// （按 属性+选项 找商品，<c>ProductId</c> 覆盖在末尾避免回表）；
/// <c>IX_PAV_Number</c> 是数值区间的扫描路径。
/// </para>
/// </remarks>
public sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValueRecord>
{
    /// <summary>CK-5 名称（§4.1 按名对账，不得改名）。</summary>
    public const string ExactlyOneCheckName = "CK_PAV_ExactlyOne";

    /// <summary>CK-6 名称。注意 §4.1 清单名是 <c>CK_PAV_BoolRange</c>（§4.2 示例里简写成 <c>CK_PAV_Bool</c>，以清单为准）。</summary>
    public const string BoolRangeCheckName = "CK_PAV_BoolRange";

    public void Configure(EntityTypeBuilder<ProductAttributeValueRecord> builder)
    {
        builder.ToTable("ProductAttributeValue", t =>
        {
            t.HasCheckConstraint(
                ExactlyOneCheckName,
                "(CASE WHEN \"OptionId\" IS NOT NULL THEN 1 ELSE 0 END"
                + " + CASE WHEN \"NumberValue\" IS NOT NULL THEN 1 ELSE 0 END"
                + " + CASE WHEN \"TextValue\" IS NOT NULL THEN 1 ELSE 0 END"
                + " + CASE WHEN \"BoolValue\" IS NOT NULL THEN 1 ELSE 0 END) = 1");

            t.HasCheckConstraint(
                BoolRangeCheckName,
                "\"BoolValue\" IS NULL OR \"BoolValue\" IN (0, 1)");
        });

        builder.HasKey(v => v.Id);

        builder.Property(v => v.ProductId).IsRequired();
        builder.Property(v => v.AttributeDefinitionId).IsRequired();
        builder.Property(v => v.TextValue).HasMaxLength(1000);

        builder.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttributeDefinitionRecord>()
            .WithMany()
            .HasForeignKey(v => v.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AttributeOptionRecord>()
            .WithMany()
            .HasForeignKey(v => v.OptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(v => new { v.AttributeDefinitionId, v.OptionId, v.ProductId })
            .HasDatabaseName("IX_PAV_Facet");

        builder.HasIndex(v => v.ProductId)
            .HasDatabaseName("IX_PAV_Product");

        builder.HasIndex(v => new { v.AttributeDefinitionId, v.NumberValue })
            .HasDatabaseName("IX_PAV_Number");

        // 部分唯一索引：同一商品同一属性不重复选同一个选项。
        // 文本/数值型属性的 OptionId 为 NULL，不参与本约束（多行是合法的，如 MultiEnum）
        builder.HasIndex(v => new { v.ProductId, v.AttributeDefinitionId, v.OptionId })
            .IsUnique()
            .HasFilter("\"OptionId\" IS NOT NULL")
            .HasDatabaseName("UX_PAV_Product_Def_Option");
    }
}
