namespace OpenStore.Application.Abstractions;

/// <summary>
/// 事务边界（后端设计 §1.5：<b>一个用例 = 一个事务</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 存在的意义不是"包一层 SaveChanges"，而是让"跨聚合的写"有唯一正确写法。
/// 典型场景：发布商品要同时改 <c>Product.Status</c>、<c>PublishedAt</c>、价格投影列，
/// 并写一条审计——三者必须同生共死。若没有这个端口，端点里就会各写各的 SaveChanges，
/// 一旦中途失败就留下"已上架但没有审计"的半截状态。
/// </para>
/// <para>
/// <b>Api 层禁止出现 <c>.SaveChangesAsync</c> / <c>.BeginTransaction</c></b>（门禁
/// Category=Layering 扫源码），就是为了逼出"写操作必须经过用例"。
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>提交当前工作单元内的全部变更。</summary>
    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// 在单个数据库事务里执行 <paramref name="action"/>；异常时回滚并原样抛出。
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
