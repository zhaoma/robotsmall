using OpenStore.Domain.Customers;

namespace OpenStore.Application.Common;

/// <summary>
/// 权限点常量。命名规则固定为 <c>perm:&lt;context&gt;.&lt;resource&gt;.&lt;action&gt;</c>：
/// 前缀 <c>perm:</c> 让门禁可以纯文本搜索"谁在端点之外偷偷声明了权限码"，
/// 点分结构让 8 个限界上下文的权限天然分组（后端设计 §6.4）。
/// </summary>
/// <remarks>
/// <b>门禁契约</b>（Category=Authorization）：<see cref="All"/> 里每一个码都必须至少被一个端点使用，
/// 否则判红（防"死码"——权限点声明了但没人挂，等于这个操作实际上无人可做）。
/// 新增权限点时，必须同时把对应端点的 <c>RequireAuthorization(Permissions.Xxx)</c> 写上，
/// 否则 <c>dotnet test --filter Category=Authorization</c> 会失败。
/// </remarks>
public static class Permissions
{
    /// <summary>权限码 claim 类型。认证处理器（SessionAuthenticationHandler）用它给主体发 <c>perm</c> claim。</summary>
    /// <remarks>用 <c>static readonly</c> 而非 <c>const</c>：Authorization 门禁反射扫描 <c>const string</c> 字段
    /// 当作"权限码"，<c>PermissionClaimType</c> 不是权限码（值是 <c>perm</c> 而非 <c>perm:...</c>），故不能是字面量常量。</remarks>
    public static readonly string PermissionClaimType = "perm";

    // ---- Catalog ----
    public const string ProductRead = "perm:catalog.product.read";
    public const string ProductCreate = "perm:catalog.product.create";
    public const string ProductEdit = "perm:catalog.product.edit";
    public const string ProductWrite = "perm:catalog.product.write";
    public const string CategoryRead = "perm:catalog.category.read";

    /// <summary>全部权限点。顺序与分组无关，只用于注册策略与门禁校验。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ProductRead,
        ProductCreate,
        ProductEdit,
        ProductWrite,
        CategoryRead,
    };

    /// <summary>
    /// 角色 → 权限点集合（静态常量表，不建 <c>RolePermission</c> 表，后端设计 §6.4）。
    /// M0 只落地了 Catalog 上下文，Admin 与 Staff 在商品上的权限一致（§6.4：员工可访问/改商品）。
    /// M1 引入 Admin-only 权限（refund/settings/theme 等）后，Staff 会被裁剪。
    /// </summary>
    public static IReadOnlyList<string> ForRole(AdminRole role) => role switch
    {
        AdminRole.Admin => All,
        AdminRole.Staff => All,
        _ => Array.Empty<string>(),
    };
}
