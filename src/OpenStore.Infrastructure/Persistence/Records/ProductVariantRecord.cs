namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductVariant</c> 表的持久化模型。
/// </summary>
/// <remarks>
/// <para>
/// <b>价格内联为列</b>（<see cref="Currency"/> + <see cref="AmountMinor"/>），与数据模型 §D11 一致：
/// 本期不建 <c>VariantPrice</c> 子表，二期以"新增 1:N 表 + 回填 + 双写"的方式做纯增量迁移。
/// 领域侧的 <c>Prices</c> 是多币种字典，M0 落库时要求"恰好一个币种"，见
/// <c>ProductMapping</c> 的说明。
/// </para>
/// <para>
/// <b><see cref="Status"/> 与 <see cref="Position"/> 是变体矩阵编辑器的直接依赖</b>（C7）：
/// 前者决定"停用变体不进前台可选集"（INV-P09），后者决定选择器的展示顺序。
/// 缺了它们，M1 的矩阵编辑器无法表达"这个红色停售了"和"尺码按 S/M/L 排"。
/// </para>
/// <para>
/// <b><see cref="CompareAtMinor"/> 是划线价</b>（原价，用于展示折扣）。
/// CK-4 强制它 ≥ <see cref="AmountMinor"/>：划线价比售价还低是数据错误，
/// 而前端会把它渲染成"立减负数"，看起来像 bug 而不是脏数据。
/// </para>
/// </remarks>
public sealed class ProductVariantRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string AxisSignature { get; set; } = string.Empty;

    /// <summary>存 <see cref="Domain.Catalog.VariantStatus"/> 的数值。默认 <c>Enabled</c>(0)。</summary>
    public int Status { get; set; }

    /// <summary>矩阵生成顺序（按选项序），决定前台选择器的排列。</summary>
    public int Position { get; set; }

    /// <summary>变体主图。FK → <c>ProductImage</c>，<c>Restrict</c>。</summary>
    public Guid? MainImageId { get; set; }

    /// <summary>条码。<b>普通索引不唯一</b>：不同供应商可能共用条码，唯一约束会误拦。</summary>
    public string? Barcode { get; set; }

    /// <summary>变体重量（克）；空则回退 <c>Product.WeightGram</c>。</summary>
    public int? WeightGram { get; set; }

    public string Currency { get; set; } = string.Empty;

    public long AmountMinor { get; set; }

    /// <summary>划线价（原价），可空。CK-4 保证 ≥ <see cref="AmountMinor"/>。</summary>
    public long? CompareAtMinor { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>乐观锁版本号，由 <c>AuditFieldsInterceptor</c> 维护。</summary>
    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ProductRecord? Product { get; set; }
}
