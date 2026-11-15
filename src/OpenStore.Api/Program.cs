using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenStore.Api.Auth;
using OpenStore.Api.Endpoints;
using OpenStore.Api.Endpoints.Admin;
using OpenStore.Api.Endpoints.Storefront;
using OpenStore.Api.Middlewares;
using OpenStore.Application.Catalog.Commands;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Application.Common;
using OpenStore.Infrastructure.Persistence;
using OpenStore.Infrastructure.Persistence.Repositories;

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

// ---- 应用层用例 ----
builder.Services.AddScoped<StorefrontProductQueryService>();
builder.Services.AddScoped<AdminProductQueryService>();
builder.Services.AddScoped<ProductAdminService>();

// ---- 认证/授权 ----
// ⚠ M0 占位：DevAuthHandler 信任 X-Dev-Perms 请求头。M1 换成 HttpOnly Cookie + DB Session
//   （SessionAuthenticationHandler），并删除此处 AddScheme 与 Auth/DevAuthHandler.cs。
builder.Services
    .AddAuthentication(DevAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(
        DevAuthHandler.SchemeName,
        configureOptions: null);

// 权限码即策略名：端点写 RequireAuthorization(Permissions.ProductWrite)，
// 这里按 Permissions.All 逐个注册策略，二者由 Category=Authorization 门禁强制同步（防死码）。
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(DevAuthHandler.PermissionClaimType, permission));
    }
});

var app = builder.Build();

app.UseMiddleware<DomainExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapStorefrontProductEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminCategoryEndpoints();

// 开发期自动迁移：让 `dotnet run` 之后 curl 就能拿到 200/404 而不是 500（"no such table"）。
// 生产环境**不**自动迁移：迁移由部署流水线执行 SQL 脚本（部署文档 §5），
// 否则一次多实例滚动发布会变成多个实例并发 alter table。
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:AutoMigrate", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

/// <summary>
/// 供 <c>WebApplicationFactory&lt;Program&gt;</c> 使用的入口标记（M1 集成测试的挂载点）。
/// 没有它，测试无法引用 top-level statements 生成的隐式 Program 类。
/// </summary>
public partial class Program
{
}
