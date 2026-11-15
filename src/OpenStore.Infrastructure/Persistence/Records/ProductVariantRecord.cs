namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>ProductVariant</c> 表的持久化模型。
/// </summary>
/// <remarks>
/// <b>价格内联为列</b>（<see cref="Currency"/> + <see cref="AmountMinor"/>），与数据模型 §D11 一致：
/// 本期不建 <c>VariantPrice</c> 子表，二期以"新增 1:N 表 + 回填 + 双写"的方式做纯增量迁移。
/// 领域侧的 <c>Prices</c> 是多币种字典，M0 落库时要求"恰好一个币种"，见
/// <c>ProductMapping</c> 的说明。
/// </remarks>
public sealed class ProductVariantRecord
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string AxisSignature { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public long AmountMinor { get; set; }

    public ProductRecord? Product { get; set; }
}
