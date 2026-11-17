namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>AuditLog</c> 表：只追加的审计流水。<b>不可变表：无 <c>UpdatedAt</c></b>。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="OccurredAt"/> 与 <see cref="CreatedAt"/> 都存，看似冗余，实际是两个事实</b>：
/// 前者是「业务何时发生」（数据回放/补录时与写入时刻不同），后者是「何时入的库」。
/// 排查时"这条审计是 3 天前发生的还是 3 天前才被补录的"是必须能回答的问题。
/// </para>
/// <para>
/// <b><see cref="BeforeJson"/> / <see cref="AfterJson"/> 只存字段白名单差异</b>，
/// <b>绝不整实体序列化</b>：整实体序列化会把成本价、内部备注、渠道凭据一并写进审计表，
/// 而审计表是最容易被导出/分享的一张表（合规追溯）。白名单在应用层构造，不在这里兜底 ——
/// 这里能做的只是"列存在但不强制"。
/// </para>
/// <para>
/// <b><see cref="IpHash"/> 存哈希不存原始 IP</b>：审计要回答"是同一个来源吗"，不需要回答"具体哪个 IP"。
/// 存原文会让审计表成为一份额外的用户位置记录，扩大泄露面。
/// </para>
/// </remarks>
public sealed class AuditLogRecord
{
    public Guid Id { get; set; }

    /// <summary>业务发生时刻（可来自导入/回放，与 <see cref="CreatedAt"/> 不同）。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>存 <see cref="Domain.Shared.AuditActorType"/> 的数值。</summary>
    public int ActorType { get; set; }

    public Guid? ActorId { get; set; }

    /// <summary>主体标签快照（邮箱/用户名），账号被删后仍可读。</summary>
    public string? ActorLabel { get; set; }

    /// <summary>稳定动作码，如 <c>product.price_changed</c> / <c>admin.created</c>。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>对象类型（表名或业务类型名）。</summary>
    public string ObjectType { get; set; } = string.Empty;

    public Guid ObjectId { get; set; }

    /// <summary>对象的人类可读标识（SKU / 邮箱）。</summary>
    public string? ObjectLabel { get; set; }

    /// <summary>变更前字段白名单 JSON（非整实体）。</summary>
    public string? BeforeJson { get; set; }

    /// <summary>变更后字段白名单 JSON（非整实体）。</summary>
    public string? AfterJson { get; set; }

    /// <summary>人工状态变更的必填理由。</summary>
    public string? Reason { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>来源 IP 的哈希（不存原文）。</summary>
    public string? IpHash { get; set; }

    /// <summary>不可变表：只有入库时刻。</summary>
    public DateTime CreatedAt { get; set; }
}
