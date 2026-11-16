using OpenStore.Domain.Customers;

namespace OpenStore.Application.Common;

/// <summary>
/// 权限点常量。命名固定为 <c>perm:&lt;上下文&gt;.&lt;动作&gt;</c>，<b>逐字照</b>后端设计 §6.4 矩阵。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是短式（<c>perm:catalog.write</c>）而不是长式（<c>perm:catalog.product.write</c>）</b>：
/// 权限点是<b>按资源分组</b>的，一个"改商品"的动作里包含建品、改价、改图、上下架四件事。
/// 长式会让同一件事派生出 <c>catalog.product.write</c> / <c>catalog.category.write</c> / <c>catalog.variant.write</c>，
/// 前端菜单映射必然漏掉一个，最后变成"员工看得见按钮、点了 403"。
/// </para>
/// <para>
/// <b>门禁契约（Category=Authorization）</b>：本类每一个 <c>public const string</c> 都必须至少被一个端点的
/// <c>.RequireAuthorization(Permissions.Xxx)</c> 使用，否则判红（防死码：声明了权限点却没人挂策略，
/// 等于这个操作实际上谁都做不了）。因此<b>新权限码必须与它的端点同一批进</b>，不许"先声明着，以后接"。
/// </para>
/// <para>
/// <b>本类之外禁止出现 <c>"perm:"</c> 字面量</b>（门禁逐文件扫描）：权限码是三方共用契约
/// （端点授权、前端菜单、门禁测试），散落的字面量会在改名时静默失效。
/// </para>
/// </remarks>
public static class Permissions
{
    /// <summary>
    /// 权限码的 claim 类型。认证处理器用它把权限点写进 <c>ClaimsPrincipal</c>。
    /// </summary>
    /// <remarks>
    /// 用 <c>static readonly</c> 而非 <c>const</c>：门禁把本类的 <c>const string</c> 字段当成"权限码"统计死码，
    /// 而 claim 类型的值是 <c>perm</c>（不是 <c>perm:...</c>），不能混进权限码集合。
    /// </remarks>
    public static readonly string PermissionClaimType = "perm";

    // ---- Catalog（商品域）----
    /// <summary>商品·读。Admin ✅ / Staff ✅。</summary>
    public const string CatalogRead = "perm:catalog.read";

    /// <summary>商品·写（含图片、变体、上下架）。Admin ✅ / Staff ✅（US-E8-02「员工可访问商品」）。</summary>
    public const string CatalogWrite = "perm:catalog.write";

    // ---- Taxonomy（分类/属性）----
    /// <summary>分类/属性·读。Admin ✅ / Staff ✅。</summary>
    public const string TaxonomyRead = "perm:taxonomy.read";

    // ---- Inventory（库存）----
    /// <summary>库存·读（列表与流水）。Admin ✅ / Staff ✅。</summary>
    public const string InventoryRead = "perm:inventory.read";

    /// <summary>
    /// 库存·调整（手工改在库数量）。Admin ✅ / Staff ✅（US-E8-02「可改库存」）。
    /// 与 <see cref="InventoryRead"/> 分开：这是会被审计追责的写操作，读权限不该隐含它。
    /// </summary>
    public const string InventoryAdjust = "perm:inventory.adjust";

    /// <summary>
    /// 全部权限点。顺序与分组无关，只用于注册授权策略与门禁校验。
    /// </summary>
    /// <remarks>
    /// <b>后续批次（与各自端点同批加入，不预声明 —— 预声明会立刻触发死码门禁）</b>：
    /// <c>catalog.delete</c>（商品删除，Staff ❌）、<c>taxonomy.write</c>、
    /// <c>inventory.read</c>、<c>inventory.adjust</c>、<c>adminuser.write</c>、
    /// <c>order.read</c>、<c>order.fulfill</c>、<c>order.cancel.unpaid</c>、<c>order.cancel.paid</c>、
    /// <c>refund.read</c>、<c>refund.write</c>、<c>return.read</c>、<c>return.review</c>、<c>customer.read</c>、
    /// <c>theme.read</c>、<c>theme.write</c>、<c>localization.write</c>、<c>settings.write</c>、
    /// <c>payment.configure</c>、<c>shipping.configure</c>、<c>audit.read</c>、<c>backup.export</c>、<c>dashboard.read</c>。
    /// </remarks>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CatalogRead,
        CatalogWrite,
        TaxonomyRead,
        InventoryRead,
        InventoryAdjust,
    };

    /// <summary>
    /// 角色 → 权限点集合（代码内静态常量表，<b>不建 <c>RolePermission</c> 表</b>：角色固定两种，架构 YAGNI 清单）。
    /// </summary>
    /// <remarks>
    /// 前端用返回的集合隐藏菜单，但<b>服务端 403 才是唯一权威</b>（US-E8-02「不依赖前端隐藏菜单」）。
    /// 本轮三个码在 §6.4 矩阵里 Admin 与 Staff 都有，故两者暂时相同；接入 <c>catalog.delete</c> 与
    /// <c>refund.*</c> 等 Admin-only 码时，Staff 分支必须在这里被裁掉（而不是靠前端藏按钮）。
    /// </remarks>
    public static IReadOnlyList<string> ForRole(AdminRole role) => role switch
    {
        AdminRole.Admin => All,
        AdminRole.Staff => All,
        _ => Array.Empty<string>(),
    };
}
