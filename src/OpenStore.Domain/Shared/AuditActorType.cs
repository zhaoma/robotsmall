namespace OpenStore.Domain.Shared;

/// <summary>
/// 审计日志的操作主体类型（<c>AuditLog.ActorType</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>不复用 <c>Customers.SessionActorType</c></b>：那个枚举只有 <c>Admin=0</c>/<c>Customer=1</c>
/// 两个值（会话只可能属于这两类主体），而审计要区分 <c>System</c>（后台任务/迁移写入）
/// 与 <c>Guest</c>（未登录访客触发的动作，如下单失败）。两者数值顺序也不同，
/// 合并会让历史行的含义互换 —— 这正是"枚举值只能追加不能插入"的典型场景。
/// </para>
/// </remarks>
public enum AuditActorType
{
    /// <summary>系统自身（后台任务、数据迁移、定时对账）。</summary>
    System = 0,

    /// <summary>管理员（后台操作）。</summary>
    Admin = 1,

    /// <summary>已登录的顾客（前台操作）。</summary>
    Customer = 2,

    /// <summary>未登录访客（如提交订单时的匿名动作）。</summary>
    Guest = 3,
}
