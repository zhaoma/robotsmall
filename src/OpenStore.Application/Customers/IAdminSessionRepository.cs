using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers;

/// <summary>管理员会话端口。实现放在 Infrastructure/Persistence。</summary>
public interface IAdminSessionRepository
{
    /// <summary>
    /// 按 token 哈希校验会话并返回主体信息。会话失效（撤销/过期/账号停用）返回 <c>null</c>。
    /// 校验通过时顺带做节流的滑动续期（写 <c>LastSeenAt</c>）。
    /// </summary>
    Task<AuthenticatedAdmin?> AuthenticateAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);

    /// <summary>创建会话（登录成功）。</summary>
    Task AddAsync(Session session, CancellationToken ct);

    /// <summary>撤销会话（登出）。幂等：不存在或已撤销不报错。</summary>
    Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);
}
