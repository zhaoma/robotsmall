namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台金额形状。与全局约定（后端设计 §5.1）一致：<c>{ amountMinor, currency }</c>，
/// 禁止出现 <c>amount</c> 或十进制金额。请求与响应共用这一个形状。
/// </summary>
public sealed record AdminMoneyDto(string Currency, long AmountMinor);
