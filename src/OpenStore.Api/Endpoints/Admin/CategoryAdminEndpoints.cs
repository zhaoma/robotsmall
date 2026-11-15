using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Common;

namespace OpenStore.Api.Endpoints.Admin;

/// <summary>
/// 后台分类端点。<b>每个端点都必须挂具体权限点</b>（门禁 Category=Authorization 扫描本目录）。
/// </summary>
public static class CategoryAdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/categories")
            .WithTags("Admin.Catalog");

        // GET /api/admin/categories —— 分类树
        // M0：分类聚合尚未在代码里落地（数据模型 §2 已建模 Category/CategoryTranslation/ProductCategory，
        // 但无实体与持久化），本端点只定义契约并返回空列表，前端按"无分类"渲染空态。M1 补分类聚合。
        group.MapGet("", () => Results.Ok(Array.Empty<AdminCategoryNodeDto>()))
            .WithName("AdminListCategories")
            .RequireAuthorization(Permissions.CategoryRead)
            .Produces<AdminCategoryNodeDto[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
