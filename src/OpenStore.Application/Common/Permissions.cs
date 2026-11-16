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
    /// Staff（员工）的权限点集合。<b>必须逐条显式列举，绝不能写成 <c>All</c></b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不用 <c>AdminRole.Staff =&gt; All</c></b>（那是最初的写法，已判定为缺陷）：<c>All</c> 是
    /// "本类声明过的所有码"的集合，它会随着每个新批次自动长大。而 §6.4 矩阵里
    /// <c>catalog.delete</c> / <c>refund.*</c> / <c>customer.read</c> / <c>theme.*</c> / <c>settings.write</c>
    /// 都是 <b>Admin-only</b>。写成 <c>All</c> 后，下一个批次只要把 <c>catalog.delete</c> 加进 <c>All</c>，
    /// 员工就<b>静默获得删除商品的能力</b>——没有一行代码被改、没有任何门禁会红，
    /// 因为死码门禁只检查"每个码有没有被端点用到"，它不知道谁该拥有这个码。
    /// </para>
    /// <para>
    /// <b>默认拒绝</b>：新权限码只进 <see cref="All"/>（为了注册授权策略与门禁），<b>不进这里</b>。
    /// 想给员工就必须显式加一行，改动会在 diff 里被看见——这正是我们要的摩擦。
    /// </para>
    /// <para>
    /// 本条与 <c>AuthorizationTests</c> 的「Staff 权限必须逐条列举」互为双保险：那条测试在有人把它
    /// 改回 <c>All</c> 时立刻红，本条注释解释为什么不能改回去。
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> StaffCodes = new[]
    {
        // §6.4：员工可读商品、可改商品（含图片/变体/上下架）、可读分类属性、可读库存、可调库存（US-E8-02）
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
    /// 因此这张表错了不会表现为"按钮多了一个"，而是表现为"员工点了就 200"——
    /// 这也是它必须是显式白名单而不是 <c>All</c> 的原因（见 <see cref="StaffCodes"/>）。
    /// </remarks>
    public static IReadOnlyList<string> ForRole(AdminRole role) => role switch
    {
        AdminRole.Admin => All,
        AdminRole.Staff => StaffCodes,
        _ => Array.Empty<string>(),
    };
}
