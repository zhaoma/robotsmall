using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>StockItem</c> 表映射（显式，无约定推断）。
/// </summary>
/// <remarks>
/// <para>
/// <b>两条 CHECK 是"绝不超卖"的最后防线，不是文档装饰</b>（AC-08）：
/// 领域层的 <c>Reserve</c> / <c>AdjustOnHand</c> 校验会被并发与重试绕过，而 CHECK 不会。
/// 负库存与"已售量超过在库量"在这个表里<b>写不出来</b>——这比任何代码评审都可靠。
/// </para>
/// <para>
/// <b>CHECK 表达式里的标识符必须双引号</b>：PG 会把未加引号的标识符折成小写，
/// <c>OnHand</c> 变成 <c>onhand</c>，列名与实际建表时的 <c>"OnHand"</c> 不匹配，
/// 迁移在 SQLite 上能过、到了 PG 上直接炸。这类问题在本地永远复现不了，只能靠写法约束。
/// </para>
/// <para>
/// 两条约束的分工：<c>CK_StockItem_NonNegative</c> 拦住任一分量为负；
/// <c>CK_StockItem_NoOversell</c> 拦住"管理员把 OnHand 调到 Reserved+Sold 以下"
/// ——后者是"<c>Sold</c> 仍占用 <c>OnHand</c>"这一建模换来的能力，缺了它，库存调整路径就能造成超卖。
/// </para>
/// </remarks>
public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItemRecord>
{
    public void Configure(EntityTypeBuilder<StockItemRecord> builder)
    {
        // 约束名按数据模型 §4.1 的 CK-1 / CK-2。名字不是装饰：SQLite 违反时报的是
        // `CHECK constraint failed: <名字>`，拆成两条并各自命名，才能一眼区分"负库存"与"超卖"。
        // 也不许"顺手取个更清楚的名字"——§4.1.1 的门禁按名对账（`CONSTRAINT "CK_…"` 计数 + 去重）。
        builder.ToTable("StockItem", table =>
        {
            table.HasCheckConstraint(
                "CK_StockItem_NoOversell",
                "\"OnHand\" >= \"Reserved\" + \"Sold\"");

            table.HasCheckConstraint(
                "CK_StockItem_NonNegative",
                "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Sold\" >= 0");
        });

        // 一变体一行：VariantId 既是 PK 也是业务键，不另设自增 Id（多一列就多一个"哪个才是真键"的问题）
        builder.HasKey(s => s.VariantId);

        // 主键由应用侧生成（数据模型 §1.1 / 对照表 T1）。不写这一句 EF 会在 Guid 未赋值时
        // 客户端生成一个——于是"忘了赋值"从编译期问题变成"看起来成功、其实插了个孤儿变体 id"。
        builder.Property(s => s.VariantId).ValueGeneratedNever();

        builder.Property(s => s.OnHand).IsRequired();
        builder.Property(s => s.Reserved).IsRequired();
        builder.Property(s => s.Sold).IsRequired();

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // 外键 → ProductVariant（Restrict）。
        // 「软删变体要保留历史流水，所以不能建 FK」是个错误推论：Restrict 只在物理 DELETE 时生效，
        // 而软删是 UPDATE IsDeleted=1，两者不冲突。真实风险是反过来 —— 没有 FK 时，
        // 一旦有人物理删了变体行，库存就成了永远悬空、DB 也不会拦的记录。
        builder.HasOne<ProductVariantRecord>()
            .WithOne()
            .HasForeignKey<StockItemRecord>(s => s.VariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // 乐观锁：并发下两个请求同时改同一变体，后提交者拿到 DbUpdateConcurrencyException（应用层重试 1 次）
        builder.Property(s => s.Version)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
