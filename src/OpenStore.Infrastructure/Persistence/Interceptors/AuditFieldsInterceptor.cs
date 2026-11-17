using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenStore.Infrastructure.Persistence.Interceptors;

/// <summary>
/// 审计尾部三件套的统一写入点：<c>CreatedAt</c> / <c>UpdatedAt</c> / <c>Version</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须有一个统一写入点</b>（对照表 C11 的第三项）：这三列如果靠"每个仓储自己记得填"，
/// 结果一定是「大部分填了、个别忘了」。而它们忘掉的后果都不报错：
/// </para>
/// <list type="bullet">
/// <item><c>CreatedAt</c> 为 <c>0001-01-01</c> —— 列表按时间排序时这些行永远沉底，看起来像"没有数据"；</item>
/// <item><c>UpdatedAt</c> 不更新 —— 后台"最后修改时间"永远显示第一次保存的时间，排障时误导人；</item>
/// <item><b><c>Version</c> 停在 0</b> —— 这是最危险的一种「配了但没生效」：乐观锁的
/// <c>WHERE Version = 0</c> 永远命中，两个管理员同时改同一行时后写者<b>静默覆盖</b>前者的价格/状态，
/// 没有任何报错、没有任何日志。</item>
/// </list>
/// <para>
/// <b>为什么用反射按属性名匹配，而不是定义一个 <c>IAuditable</c> 接口</b>：
/// <c>StockItemRecord</c> 的时间列是 <see cref="DateTimeOffset"/>（领域侧要保留偏移用于展示），
/// 其余表是 <see cref="DateTime"/>（库里只存 UTC 数值）。接口要求同一签名，会逼着其中一边改类型；
/// 而按属性名 + 类型分派能同时覆盖两种，且<b>新增表时零改动</b>——只需照约定命名。
/// 代价是每实体类型一次反射，用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 缓存属性元数据后
/// 只在首次写入时付出。
/// </para>
/// <para>
/// <b>版本号的语义</b>：<c>Added</c> 时置 <c>1</c>（不是 0），<c>Modified</c> 时 <c>++</c>。
/// 用 1 作为初值是为了让"从未被任何拦截器处理过"（值为 <c>0</c>）成为一个<b>可识别的异常态</b>
/// —— 如果初值也是 0，那么"拦截器没生效"与"新插入的行"就无法区分了。
/// </para>
/// </remarks>
public sealed class AuditFieldsInterceptor : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<Type, AuditShape> ShapeCache = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // 同一批 SaveChanges 里的所有行共用同一个时间戳：让"同一次业务动作写入的多行"
        // 在审计上可识别（否则逐行取 Now 会得到相差几毫秒的一堆值，复盘时无法确认是一批）
        var nowUtc = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var shape = ShapeCache.GetOrAdd(entry.Entity.GetType(), BuildShape);
            ApplyShape(entry, shape, nowUtc);
        }
    }

    private static void ApplyShape(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, AuditShape shape, DateTime nowUtc)
    {
        if (entry.State == EntityState.Added && shape.CreatedAt is not null)
        {
            SetTime(entry.Entity, shape.CreatedAt, nowUtc);
        }

        if (shape.UpdatedAt is not null)
        {
            SetTime(entry.Entity, shape.UpdatedAt, nowUtc);
        }

        if (shape.Version is not null)
        {
            var current = shape.Version.GetValue(entry.Entity) is long v ? v : 0L;
            // Added 从 0 → 1；Modified 自增。若外部显式设过非 0 初值（如测试构造），保留它
            shape.Version.SetValue(entry.Entity, entry.State == EntityState.Added ? (current == 0 ? 1L : current) : current + 1);
        }
    }

    private static void SetTime(object entity, PropertyInfo property, DateTime nowUtc)
    {
        if (property.PropertyType == typeof(DateTimeOffset) || property.PropertyType == typeof(DateTimeOffset?))
        {
            property.SetValue(entity, new DateTimeOffset(nowUtc, TimeSpan.Zero));
            return;
        }

        property.SetValue(entity, nowUtc);
    }

    private static AuditShape BuildShape(Type type)
    {
        // 只看 public 实例可写属性；Record 类都是 POCO，无索引器
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

        return new AuditShape(
            CreatedAt: Find(props, nameof(AuditShape.CreatedAt)),
            UpdatedAt: Find(props, nameof(AuditShape.UpdatedAt)),
            Version: Find(props, nameof(AuditShape.Version)));
    }

    private static PropertyInfo? Find(PropertyInfo[] props, string name)
        => props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 一个实体类型的审计尾部形状。<b>三个都可能为 <c>null</c></b>：
    /// 不可变表（如 <c>MediaAsset</c>、<c>StockLedger</c>）刻意没有 <c>UpdatedAt</c>，
    /// 复合主键表（如 <c>AttributeSetMember</c>）没有 <c>Version</c>。拦截器按"有则填"工作，
    /// 不要求每张表都齐三列 —— 强制齐反而会造出"为了满足拦截器而加一列没人读"的假列。
    /// </summary>
    private sealed record AuditShape(PropertyInfo? CreatedAt, PropertyInfo? UpdatedAt, PropertyInfo? Version);
}
