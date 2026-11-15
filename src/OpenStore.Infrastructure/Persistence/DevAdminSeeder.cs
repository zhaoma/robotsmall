using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenStore.Application.Abstractions;
using OpenStore.Domain.Customers;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// 开发期种子数据：确保本地至少有一个可登录的管理员（后端设计 §6.8 的安装向导落地前，
/// 登录功能需要一个入口账号）。<b>仅在 Development 环境由组合根调用，生产不种。</b>
/// </summary>
public static class DevAdminSeeder
{
    public const string DefaultEmail = "admin@openstore.local";

    public static async Task SeedAsync(IServiceProvider services, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Dev admin password must not be empty.", nameof(password));
        }

        var db = services.GetRequiredService<StoreDbContext>();
        if (await db.AdminUsers.AsNoTracking().AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var passwordHasher = services.GetRequiredService<IPasswordHasher>();
        var admin = AdminUser.Create(
            Guid.NewGuid(),
            DefaultEmail,
            passwordHasher.Hash(password),
            "Administrator",
            AdminRole.Admin,
            DateTimeOffset.UtcNow);

        db.AdminUsers.Add(AdminUserMapping.ToRecord(admin));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
