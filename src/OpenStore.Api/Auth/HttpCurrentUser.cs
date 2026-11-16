using System.Security.Claims;
using OpenStore.Application.Abstractions;
using OpenStore.Application.Common;
using OpenStore.Domain.Customers;

namespace OpenStore.Api.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> 的 HTTP 实现：只从 <see cref="ClaimsPrincipal"/> 读身份。
/// </summary>
/// <remarks>
/// <para>
/// <b>所有"这是谁"的判断只能经过这里</b>（后端设计 §6.5）：请求体与查询串里既没有
/// <c>adminId</c> 也没有 <c>customerId</c> 字段，因此"改个参数就能操作别人的数据"在类型层面不成立。
/// </para>
/// <para>
/// <b>身份来源是会话 Cookie → DB Session → AdminUser</b>，由 <see cref="SessionAuthenticationHandler"/>
/// 每个请求校验一次并落到 Claim 上（封禁/改密立即生效的落点）。本类<b>不查库</b>：
/// 再查一次等于把同一个事实读两遍，且会在每个请求上多加一次 DB 往返。
/// </para>
/// <para>
/// 解析失败一律按"未登录"处理而不是抛异常：身份信息异常时应该得到 401，
/// 而不是 500 —— 后者会让一次 Cookie 损坏看起来像服务端故障。
/// </para>
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public AdminRole? Role
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<AdminRole>(raw, ignoreCase: false, out var role) ? role : null;
        }
    }

    public IReadOnlySet<string> Permissions
    {
        get
        {
            var principal = Principal;
            if (principal is null)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            // ToHashSet 每次访问都会重建：这里刻意不做缓存字段。
            // 权限集合是每请求不变的，代价只是几条 claim 的拷贝；而缓存字段会把"实例生命周期"
            // 与"请求生命周期"绑在一起——一旦有人在别处注册成 Singleton，就会串请求身份。
            // 这里必须写全限定名：本类的 Permissions 属性与 static class Permissions 同名，
            // 在属性体内裸写 Permissions 会解析成属性本身（IReadOnlySet<string>），编译错误很难一眼看出。
            return principal.FindAll(global::OpenStore.Application.Common.Permissions.PermissionClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? DisplayName => Principal?.FindFirstValue(ClaimTypes.Name);

    public bool HasPermission(string permission)
        => !string.IsNullOrWhiteSpace(permission) && Permissions.Contains(permission);

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
}
