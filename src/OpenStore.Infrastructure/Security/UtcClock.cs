using OpenStore.Application.Abstractions;

namespace OpenStore.Infrastructure.Security;

/// <summary>真实时钟。<see cref="IClock.UtcNow"/> 返回系统 UTC 时间。</summary>
/// <remarks>命名为 <c>UtcClock</c> 而非 <c>SystemClock</c>：避免与 ASP.NET 的 <c>Microsoft.AspNetCore.Authentication.SystemClock</c> 撞名。</remarks>
public sealed class UtcClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
