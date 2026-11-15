namespace OpenStore.Application.Abstractions;

/// <summary>
/// 可替换的时钟。<b>业务代码一律用它取"现在"，不直接调 <see cref="DateTimeOffset.UtcNow"/></b>。
/// </summary>
/// <remarks>
/// 理由很具体：会话空闲/绝对过期、优惠券有效期、订单超时释放都依赖"现在"，
/// 而集成测试要构造"31 天后再来看这个会话"的场景。直接调静态属性会让这些测试只能靠
/// <c>Thread.Sleep</c> 或反射改系统时间——前者慢且不稳，后者污染整台机器。
/// </remarks>
public interface IClock
{
    /// <summary>当前 UTC 时间。</summary>
    DateTimeOffset UtcNow { get; }
}
