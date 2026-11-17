using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>AuditLog</c> 表映射（只追加、不可变：无 <c>UpdatedAt</c>、无软删列）。
/// </summary>
/// <remarks>
/// <b>四条索引全部按"倒序时间窗 + 一个前导过滤列"的查询形态定</b>（数据模型 §1.5.1）：
/// <c>IX_Audit_Object</c> 服务"这个对象改过什么"（详情页的变更历史）；
/// <c>IX_Audit_Actor</c> 服务"这个人做过什么"（离职审计）；
/// <c>IX_Audit_Action</c> 服务"这类动作发生过几次"（如统计改价次数）；
/// <c>IX_Audit_OccurredAt</c> 服务"最近发生了什么"（默认视图，无过滤）。
/// 四个查询都不带 <c>Action = ?</c> 之外的复合条件，所以不必再做覆盖索引。
/// </remarks>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogRecord>
{
    public void Configure(EntityTypeBuilder<AuditLogRecord> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.ActorType).IsRequired();
        builder.Property(a => a.ActorLabel).HasMaxLength(200);
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ObjectType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ObjectId).IsRequired();
        builder.Property(a => a.ObjectLabel).HasMaxLength(100);
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpHash).HasMaxLength(64);

        // BeforeJson/AfterJson 不设 MaxLength：字段白名单差异的长度随实体规模增长，
        // 截断会把 JSON 切坏（读出来解析失败比"太长了"更麻烦）。用 TEXT 不限制。

        builder.HasIndex(a => new { a.ObjectType, a.ObjectId, a.OccurredAt })
            .HasDatabaseName("IX_Audit_Object");

        builder.HasIndex(a => new { a.ActorType, a.ActorId, a.OccurredAt })
            .HasDatabaseName("IX_Audit_Actor");

        builder.HasIndex(a => new { a.Action, a.OccurredAt })
            .HasDatabaseName("IX_Audit_Action");

        builder.HasIndex(a => a.OccurredAt)
            .HasDatabaseName("IX_Audit_OccurredAt");
    }
}
