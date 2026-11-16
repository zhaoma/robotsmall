using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>StockLedger</c> 表映射（显式，无约定推断）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本表只追加：仓储不得暴露 Update / Delete。</b>没有 <c>UpdatedAt</c>，没有软删除列，
/// 物理上不存在"删流水"的语义。纠正错误只能追加反向流水，让两次动作都留在历史里
/// （数据模型 §3.2「流水表如何保证可追溯且不可改」）。
/// </para>
/// <para>
/// <b>索引是按查询形态定的，不是"多建几个不吃亏"</b>（名字照数据模型 §4.2 / 对照表 §1.3.2）：
/// <list type="bullet">
/// <item><c>IX_StockLedger_Variant_Id</c> <c>(VariantId, Id)</c> —— 后台"某变体的流水，按时间倒序分页"，
/// <c>Id</c> 自增即时间序，避免额外排序列；</item>
/// <item><c>IX_StockLedger_ChangeType_Created</c> <c>(ChangeType, CreatedAt)</c> —— 对账/排障按类型扫区间
/// （如"今天所有 Ship"）；</item>
/// <item><c>IX_StockLedger_Order</c> <c>(OrderId)</c> —— 从订单反查库存足迹（<c>OrderId</c> 可为 null，
/// SQLite 与 PG 对多 NULL 唯一索引行为不同，故这里<b>不</b>做唯一约束，只做普通索引）；</item>
/// <item><c>IX_StockLedger_CreatedAt</c> <c>(CreatedAt)</c> —— 按时间窗聚合（对账任务扫"昨天"）。</item>
/// </list>
/// </para>
/// <para>
/// <b>列名 <c>CreatedAt</c> 与 CLR 属性 <c>OccurredAt</c> 的差异收敛在这里</b>：文档定的是
/// <c>CreatedAt</c>（不可变表只有这一个时间列），而领域侧的 <c>StockMovement</c> 一直叫
/// <c>OccurredAt</c>（"发生时间"比"创建时间"准确）。用 <c>HasColumnName</c> 把它钉在一处，
/// 而不是让两个名字各自扩散到第三、第四个地方——索引名 <c>…_ChangeType_Created</c> 也才对得上它真指的列。
/// </para>
/// </remarks>
public sealed class StockLedgerConfiguration : IEntityTypeConfiguration<StockLedgerRecord>
{
    public void Configure(EntityTypeBuilder<StockLedgerRecord> builder)
    {
        builder.ToTable("StockLedger");

        builder.HasKey(l => l.Id);

        // 自增：Id 即时间序。流水不提供客户端指定主键的入口（那会让"顺序"变成可伪造的事实）
        builder.Property(l => l.Id)
            .ValueGeneratedOnAdd();

        builder.Property(l => l.VariantId).IsRequired();
        builder.Property(l => l.ChangeType).IsRequired();

        builder.Property(l => l.OnHandDelta).IsRequired();
        builder.Property(l => l.ReservedDelta).IsRequired();
        builder.Property(l => l.SoldDelta).IsRequired();

        builder.Property(l => l.OnHandAfter).IsRequired();
        builder.Property(l => l.ReservedAfter).IsRequired();
        builder.Property(l => l.SoldAfter).IsRequired();

        builder.Property(l => l.Reason).HasMaxLength(500);
        builder.Property(l => l.OperatorType).IsRequired();
        builder.Property(l => l.OperatorName).HasMaxLength(200);
        builder.Property(l => l.CorrelationId).HasMaxLength(64);

        // DB 列名 CreatedAt（对照表 §1.3.2 / 02 §3.2），CLR 属性沿用领域命名 OccurredAt
        builder.Property(l => l.OccurredAt)
            .HasColumnName("CreatedAt")
            .IsRequired();

        // 外键 → ProductVariant（Restrict）：流水是"变体的账"，变体被物理删掉后账就再也对不上了。
        // 与 StockItem 同款。「软删变体要保留历史流水，所以不能建 FK」是错误推论：Restrict 只在
        // 物理 DELETE 时生效，而软删是 UPDATE IsDeleted=1，两者不冲突。
        builder.HasOne<ProductVariantRecord>()
            .WithMany()
            .HasForeignKey(l => l.VariantId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(l => new { l.VariantId, l.Id })
            .HasDatabaseName("IX_StockLedger_Variant_Id");

        builder.HasIndex(l => new { l.ChangeType, l.OccurredAt })
            .HasDatabaseName("IX_StockLedger_ChangeType_Created");

        builder.HasIndex(l => l.OccurredAt)
            .HasDatabaseName("IX_StockLedger_CreatedAt");

        builder.HasIndex(l => l.OrderId)
            .HasDatabaseName("IX_StockLedger_Order");
    }
}
