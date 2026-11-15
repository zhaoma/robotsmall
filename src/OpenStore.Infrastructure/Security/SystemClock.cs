using OpenStore.Application.Abstractions;

namespace OpenStore.Infrastructure.Security;

/// <summary>真实时钟。<see cref="IClock.UtcNow"/> 返回系统 UTC 时间。</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
