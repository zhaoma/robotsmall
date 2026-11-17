using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>ProductVariant</c> 表映射。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一索引 <c>(ProductId, AxisSignature)</c> 是"变体矩阵不重复"的最后一道防线</b>：
/// 领域层先拦（好报错），DB 再拦（防并发/绕过用例的直接写入）。
/// </para>
/// <para>
/// <b>FK 是 <c>Restrict</c> 而不是 <c>Cascade</c></b>（C8，02 §3.1 明写）：
/// <c>Cascade</c> 会让"删商品忘记删变体"这类聚合边界破损静默通过（数据库替你删了，
/// 你永远发现不了）。变体虽是聚合内实体，但<b>被库存流水按 Id 引用</b>，不算"纯从属行"，
/// 不在 02 §5-9 的 <c>Cascade</c> 白名单里。
/// 副作用：物理删商品必须先删变体 —— 实际不会发生（商品只软删），
/// 只在测试/重置路径需要按序删。
/// </para>
/// <para>
/// <b>两条 CHECK 在 <c>CREATE TABLE</c> 内联</b>（CK-3/CK-4），因此完全绕开
/// SQLite 不支持 <c>ALTER TABLE ADD CONSTRAINT</c> 的限制。
/// </para>
/// </remarks>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariantRecord>
{
    /// <summary>CK-3 名称（§4.1 按名对账，不得改名）。</summary>
    public const string AmountPositiveCheckName = "CK_ProductVariant_AmountMinor_Positive";

    /// <summary>CK-4 名称。</summary>
    public const string CompareAtCheckName = "CK_ProductVariant_CompareAt";

    public void Configure(EntityTypeBuilder<ProductVariantRecord> builder)
    {
        builder.ToTable("ProductVariant", t =>
        {
            t.HasCheckConstraint(AmountPositiveCheckName, "\"AmountMinor\" > 0");

            // 显式处理 NULL：不能写成隐含"结果为 NULL 视为通过"的形态（02 §5-8 纪律）
            t.HasCheckConstraint(
                CompareAtCheckName,
                "\"CompareAtMinor\" IS NULL OR \"CompareAtMinor\" >= \"AmountMinor\"");
        });

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Sku)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(v => v.AxisSignature)
            .HasMaxLength(400)
            .IsRequired();

        // 币种固定 3 位（ISO 4217），不用枚举整数：改枚举会污染所有历史数据
        builder.Property(v => v.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(v => v.AmountMinor)
            .IsRequired();

        builder.Property(v => v.Status).IsRequired();
        builder.Property(v => v.Position).IsRequired();
        builder.Property(v => v.Barcode).HasMaxLength(64);
        builder.Property(v => v.IsDeleted).IsRequired();

        builder.Property(v => v.Version)
            .IsRequired()
            .IsConcurrencyToken();

        // C8：Restrict（理由见类型注释）
        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // C10：索引名按 02 §4 规范（UX_ = 唯一，IX_ = 普通）
        builder.HasIndex(v => new { v.ProductId, v.AxisSignature })
            .IsUnique()
            .HasDatabaseName("UX_Variant_Product_AxisSignature");

        builder.HasIndex(v => v.Sku)
            .IsUnique()
            .HasDatabaseName("UX_Variant_Sku");

        // 变体列表：按商品过滤 + 按状态 + 按矩阵顺序
        builder.HasIndex(v => new { v.ProductId, v.Status, v.Position })
            .HasDatabaseName("IX_Variant_Product_Status");

        // 条码录入/扫码查货。普通索引不唯一：不同供应商会共用条码
        builder.HasIndex(v => v.Barcode)
            .HasDatabaseName("IX_Variant_Barcode");

        // 一张图不能被两个变体同时当主图
        builder.HasIndex(v => v.MainImageId)
            .IsUnique()
            .HasDatabaseName("UX_Variant_MainImageId");

        // 变体主图 → ProductImage，Restrict
        builder.HasOne<ProductImageRecord>()
            .WithMany()
            .HasForeignKey(v => v.MainImageId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
