using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenStore.Application.Abstractions;
using OpenStore.Domain.Inventory;
using OpenStore.Domain.Shared;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IStockRepository"/> 的 SQLite 实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>低库存阈值由组合根传入，而不是在这里注入 <c>IConfiguration</c></b>：
/// Infrastructure 目前只引用 EF Core（见 csproj），为读一个配置项去加
/// <c>Microsoft.Extensions.Configuration.Abstractions</c> 会让"持久层依赖配置系统"变成惯例，
/// 之后每个人都往仓储里塞配置项。配置绑定属于组合根（Program.cs）的职责，这里只接收结果值。
/// </para>
/// <para>
/// <b>保存必须单事务，两件事缺一不可</b>（数据模型 §3.2）：
/// ① 条件更新三元组 <c>WHERE VariantId=@v AND Version=@original</c>；
/// ② 追加 <c>DrainMovements()</c> 的全部流水行。
/// 只写流水不改存量会让对账永久不平；只改存量不写流水则等于"有人绕过 API 改库"。
/// </para>
/// <para>
/// <b>并发保护只能在持久层闭合</b>：领域层没有锁，"读-判断-写"之间有窗口，
/// 窗口里另一个请求可能已经把 <c>Reserved</c> 加上去了。能闭合它的是 DB 的条件更新
/// （影响 0 行即冲突），而不是在应用层再查一次再判断一次。
/// </para>
/// <para>
/// <b>读用 <see cref="StockItem.Rehydrate"/>，绝不用 <see cref="StockItem.Create"/></b>：
/// <c>Create</c> 会往聚合里塞一条 <c>Initial</c> 流水，读一次就多一条流水，
/// 等于把"每次查询"记成"每次入库"，对账直接报废。
/// </para>
/// </remarks>
public sealed class SqliteStockRepository(StoreDbContext db, IClock clock, int lowStockThreshold)
    : IStockRepository
{
    /// <summary>低库存默认阈值（与前台 <c>low_stock</c> 的 5 件同口径，避免"前台说快没了、后台说不紧张"）。</summary>
    public const int DefaultLowStockThreshold = 5;

    /// <summary>
    /// 低库存阈值（组合根按 <c>Inventory:LowStockThreshold</c> 注入，缺省取
    /// <see cref="DefaultLowStockThreshold"/>）。
    /// </summary>
    /// <remarks>
    /// 不给主构造函数写默认值：C# 的默认参数在"类型参数作用域"求值，引用不到类内声明的常量
    /// （会报 CS0103）。宁可让组合根显式传值，也不要为了省一次传参在类里写两份 5。
    /// </remarks>
    private int LowStockThreshold => lowStockThreshold;

    public async Task<StockItem?> GetAsync(Guid variantId, CancellationToken ct)
    {
        var record = await db.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.VariantId == variantId, ct)
            .ConfigureAwait(false);

        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyDictionary<Guid, StockItem>> GetManyAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, StockItem>();
        }

        // 一次批量取：前台列表页 24 条商品、每商品多个变体，逐条查会变成 N+1（后端设计 §2.1 关键设计点 2）
        var records = await db.StockItems
            .AsNoTracking()
            .Where(s => variantIds.Contains(s.VariantId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return records.ToDictionary(r => r.VariantId, ToDomain);
    }

    public async Task SaveAsync(StockItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 先把待持久化流水取走（取走后聚合内清空，重复调用不会重复写流水）
        var movements = item.DrainMovements();

        // 聚合每写一条流水就把 Version++ 一次，因此"读进来时的版本"= 当前版本 − 本次流水条数。
        // 这个恒等式由 StockItem.Record 保证（一处 ++，一处 Add(_pending)）；下面守一下，
        // 万一将来这两处脱钩，会立刻炸在保存侧而不是变成静默的乐观锁失效。
        var originalVersion = item.Version - movements.Count;
        if (originalVersion < 0)
        {
            throw new InvariantViolationException(
                "STOCK_VERSION_INCONSISTENT",
                $"StockItem '{item.VariantId}' has Version {item.Version} but {movements.Count} pending movements; " +
                "version and movement count have drifted apart.");
        }

        var ownedTransaction = await BeginIfNeededAsync(ct).ConfigureAwait(false);

        var now = clock.UtcNow;

        try
        {
            var affected = await db.StockItems
                .Where(s => s.VariantId == item.VariantId && s.Version == originalVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.OnHand, item.OnHand)
                        .SetProperty(s => s.Reserved, item.Reserved)
                        .SetProperty(s => s.Sold, item.Sold)
                        .SetProperty(s => s.Version, item.Version)
                        .SetProperty(s => s.UpdatedAt, now),
                    ct)
                .ConfigureAwait(false);

            if (affected == 0)
            {
                var exists = await db.StockItems
                    .AsNoTracking()
                    .AnyAsync(s => s.VariantId == item.VariantId, ct)
                    .ConfigureAwait(false);

                if (exists)
                {
                    // 行存在但版本对不上 = 另一个请求抢先提交了。不重试：重试策略属于用例层（重试 1 次），
                    // 仓储静默重试会把"并发"变成"看不见的覆盖"。
                    throw new DomainException(
                        "CONCURRENT_UPDATE",
                        $"StockItem '{item.VariantId}' was modified by another request " +
                        $"(expected Version {originalVersion}, current row differs). Reload and retry.");
                }

                // 行不存在且聚合尚未落库（originalVersion == 0）：变体刚建、库存行还没插。
                // 这是"建库存行"的唯一入口，不需要额外的 Add 方法（多一个写入口就多一处忘记写流水的地方）。
                db.StockItems.Add(new StockItemRecord
                {
                    VariantId = item.VariantId,
                    OnHand = item.OnHand,
                    Reserved = item.Reserved,
                    Sold = item.Sold,
                    Version = item.Version,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            foreach (var movement in movements)
            {
                db.StockLedger.Add(ToRecord(item.VariantId, movement));
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        catch
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.RollbackAsync(ct).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<PagedResult<StockOverviewDto>> ListAsync(
        StockListFilter filter,
        PageRequest page,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var threshold = LowStockThreshold;

        // 三表 inner join：SKU 在变体上，标题在商品上。
        // 过滤与分页全部下推到 SQL —— 取全表再内存过滤在 1 万变体时会先慢 100 倍再 OOM。
        var joined =
            from s in db.StockItems.AsNoTracking()
            join v in db.ProductVariants.AsNoTracking() on s.VariantId equals v.Id
            join p in db.Products.AsNoTracking() on v.ProductId equals p.Id
            select new { Stock = s, Variant = v, Product = p };

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var pattern = $"%{filter.Query.Trim()}%";
            joined = joined.Where(x =>
                EF.Functions.Like(x.Variant.Sku, pattern) || EF.Functions.Like(x.Product.Title, pattern));
        }

        if (filter.LowStockOnly)
        {
            // 可用量是计算表达式，不落库：SQL 里直接算，才能走"过滤后在 DB 内分页"
            joined = joined.Where(x => x.Stock.OnHand - x.Stock.Reserved - x.Stock.Sold <= threshold);
        }

        var total = await joined.CountAsync(ct).ConfigureAwait(false);

        var items = await joined
            .OrderBy(x => x.Variant.Sku)
            .ThenBy(x => x.Stock.VariantId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new StockOverviewDto(
                x.Stock.VariantId,
                x.Variant.Sku,
                x.Product.Title,
                x.Stock.OnHand,
                x.Stock.Reserved,
                x.Stock.Sold,
                threshold,
                x.Stock.UpdatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<StockOverviewDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(
        Guid variantId,
        PageRequest page,
        CancellationToken ct)
    {
        var query = db.StockLedger
            .AsNoTracking()
            .Where(l => l.VariantId == variantId);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        // 先投影成匿名类型再在内存里转枚举：把 (StockChangeType)l.ChangeType 直接写在 IQueryable 投影里，
        // 部分 provider/版本会翻译失败（"could not be translated"），而这类错误只在运行时暴露。
        var rows = await query
            .OrderByDescending(l => l.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(l => new
            {
                l.Id,
                l.VariantId,
                l.ChangeType,
                l.OnHandDelta,
                l.ReservedDelta,
                l.SoldDelta,
                l.OnHandAfter,
                l.ReservedAfter,
                l.SoldAfter,
                l.Reason,
                l.OrderId,
                l.OperatorType,
                l.OperatorName,
                l.OccurredAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rows
            .Select(l => new StockMovementDto(
                l.Id,
                l.VariantId,
                (StockChangeType)l.ChangeType,
                l.OnHandDelta,
                l.ReservedDelta,
                l.SoldDelta,
                l.OnHandAfter,
                l.ReservedAfter,
                l.SoldAfter,
                l.Reason,
                l.OrderId,
                (StockOperatorType)l.OperatorType,
                l.OperatorName,
                l.OccurredAt))
            .ToList();

        return new PagedResult<StockMovementDto>(items, page.Page, page.PageSize, total);
    }

    private static StockItem ToDomain(StockItemRecord record)
        => StockItem.Rehydrate(record.VariantId, record.OnHand, record.Reserved, record.Sold, record.Version);

    private static StockLedgerRecord ToRecord(Guid variantId, StockMovement movement) => new()
    {
        VariantId = variantId,
        ChangeType = (int)movement.ChangeType,
        OnHandDelta = movement.OnHandDelta,
        ReservedDelta = movement.ReservedDelta,
        SoldDelta = movement.SoldDelta,
        OnHandAfter = movement.OnHandAfter,
        ReservedAfter = movement.ReservedAfter,
        SoldAfter = movement.SoldAfter,
        Reason = movement.Reason,
        OrderId = movement.OrderId,
        OrderLineId = movement.OrderLineId,
        OperatorType = (int)movement.Operator.Type,
        OperatorId = movement.Operator.Id,
        OperatorName = movement.Operator.Name,
        CorrelationId = null,
        OccurredAt = movement.OccurredAt,
    };

    /// <summary>
    /// 没有外层事务时自己开一个；有的话就加入（<c>IUnitOfWork.ExecuteInTransactionAsync</c> 场景）。
    /// </summary>
    /// <remarks>
    /// 不判断 <c>CurrentTransaction</c> 就 <c>BeginTransactionAsync</c>，在 EF 里会抛
    /// "transaction already in progress"，而这只有真正组合起来用的时候才暴露。
    /// </remarks>
    private async Task<IDbContextTransaction?> BeginIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
    }
}
