using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Application.Common;

namespace OpenStore.Api.Endpoints.Admin;

/// <summary>
/// 后台分类端点。<b>每个端点都必须挂具体权限点</b>（门禁 Category=Authorization 扫描本目录）。
/// </summary>
public static class CategoryAdminEndpoints
{
    /// <summary>译文缺省语言。与 <c>docs</c> 的默认语言口径一致（zh-Hans 为管理员默认界面语言）。</summary>
    private const string DefaultLocale = "zh-Hans";

    public static IEndpointRouteBuilder MapAdminCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/categories")
            .WithTags("Admin.Catalog");

        // GET /api/admin/categories — 分类树
        // 已接真实聚合（M1）：读 Category + CategoryTranslation，组树后返回。
        // 分类尚无写入端点（M1 只读树），所以首次调用会是空数组 —— 这是"库里确实没有分类"，
        // 不是"端点没接线"。两者的差别只有真接线才看得出来，所以这里必须查库而不是返回常量。
        group.MapGet("", async (
            string? locale,
            IAdminCategoryQuery query,
            CancellationToken ct) =>
        {
            var effective = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale.Trim();
            var tree = await query.GetTreeAsync(effective, ct).ConfigureAwait(false);
            return Results.Ok(tree);
        })
        .WithName("AdminListCategories")
        .RequireAuthorization(Permissions.TaxonomyRead)
        .Produces<AdminCategoryNodeDto[]>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
