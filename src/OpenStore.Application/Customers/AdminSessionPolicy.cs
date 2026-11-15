namespace OpenStore.Application.Customers;

/// <summary>管理员会话生命周期（后端设计 §6.2：Admin 120 分钟空闲 / 7 天绝对）。</summary>
public static class AdminSessionPolicy
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(120);

    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromDays(7);
}
