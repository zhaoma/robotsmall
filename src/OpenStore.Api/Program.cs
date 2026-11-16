using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenStore.Api.Auth;
using OpenStore.Api.Endpoints;
using OpenStore.Api.Endpoints.Admin;
using OpenStore.Api.Endpoints.AdminAuth;
using OpenStore.Api.Endpoints.Storefront;
using OpenStore.Api.Middlewares;
using OpenStore.Application.Abstractions;
using OpenStore.Application.Catalog.Commands;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Application.Common;
using OpenStore.Application.Customers;
using OpenStore.Infrastructure.Persistence;
using OpenStore.Infrastructure.Persistence.Repositories;
using OpenStore.Infrastructure.Security;

// ============================================================================
// OpenStore.Api —— 组合根（composition root）。
// 这里只做三件事：注册依赖、挂中间件、映射端点。
// 任何业务判断、任何数据库写操作都不允许出现在本文件（门禁 Category=Layering 会扫描
// src/OpenStore.Api/**/*.cs 里是否出现 .SaveChangesAsync / .BeginTransaction）。
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// 枚举按字符串进出（admin API 用 "Active" 而不是 1）：后台接口是人读的，
// 数字魔法值会让运维在 curl 时靠猜。
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- 基础设施 ----
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? StoreDbContextFactory.DevelopmentConnectionString;

builder.Services.AddDbContext<StoreDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IProductRepository, SqliteProductRepository>();
builder.Services.AddScoped<IProductAdminRepository, SqliteProductAdminRepository>();
builder.Services.AddScoped<IAdminProductQuery, SqliteAdminProductQuery>();
builder.Services.AddScoped<IStockRepository>(sp => new SqliteStockRepository(
    sp.GetRequiredService<StoreDbContext>(),
    sp.GetRequiredService<IClock>(),
    builder.Configuration.GetValue("Inventory:LowStockThreshold", SqliteStockRepository.DefaultLowStockThreshold)));
builder.Services.AddScoped<IAdminUserRepository, SqliteAdminUserRepository>();
builder.Services.AddScoped<ISessionRepository, SqliteSessionRepository>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IClock, UtcClock>();
builder.Services.AddHttpContextAccessor();

// 当前身份：只从 ClaimsPrincipal 读（ICurrentUser 是"这是谁"的唯一入口，后端设计 §6.5）
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---- 应用层用例 ----
builder.Services.AddScoped<StorefrontProductQueryService>();
builder.Services.AddScoped<AdminProductQueryService>();
builder.Services.AddScoped<ProductAdminService>();
builder.Services.AddScoped<AdminAuthService>();

// ---- 认证/授权 ----
// 管理端会话认证：HttpOnly Cookie（os_admin_sid）+ DB Session 校验（后端设计 §6.1/§6.3）。
// 已删除 M0 的 DevAuthHandler（信任 X-Dev-Perms 头的占位实现）。
builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        configureOptions: null);

// 权限码即策略名：端点写 RequireAuthorization(Permissions.Xxx)，
// 这里按 Permissions.All 逐个注册策略，二者由 Category=Authorization 门禁强制同步（防死码）。
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(Permissions.PermissionClaimType, permission));
    }
});

var app = builder.Build();

app.UseMiddleware<DomainExceptionMiddleware>();

app.UseAuthentication();

// CSRF 防护：必须在 UseAuthentication 之后（要读会话 Claim）、UseAuthorization 之前
// （无权限的请求没必要再走一遍授权）。后端设计 §6.6：Admin 端 Origin 校验 + double-submit。
app.UseMiddleware<AdminCsrfMiddleware>();

app.UseAuthorization();

app.MapHealthEndpoints();
app.MapStorefrontProductEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminCategoryEndpoints();
app.MapInventoryAdminEndpoints();
app.MapAdminAuthEndpoints();

// 开发期自动迁移：让 `dotnet run` 之后 curl 就能拿到 200/404 而不是 500（"no such table"）。
// 生产环境**不**自动迁移：迁移由部署流水线执行 SQL 脚本（部署文档 §5），
// 否则一次多实例滚动发布会变成多个实例并发 alter table。
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:AutoMigrate", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
    await db.Database.MigrateAsync();
}

// 开发期种子管理员：Session 认证需要一个可登录的入口账号（生产由安装向导创建，见后端设计 §6.8）。
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var devPassword = app.Configuration["Auth:DevAdminPassword"] ?? "ChangeMe123!";
    await DevAdminSeeder.SeedAsync(scope.ServiceProvider, devPassword, CancellationToken.None);
}

app.Run();

/// <summary>
/// 供 <c>WebApplicationFactory&lt;Program&gt;</c> 使用的入口标记（M1 集成测试的挂载点）。
/// 没有它，测试无法引用 top-level statements 生成的隐式 Program 类。
/// </summary>
public partial class Program
{
}
