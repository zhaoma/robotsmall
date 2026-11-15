using OpenStore.Application.Catalog.Commands;
using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Catalog.Queries;
using OpenStore.Application.Common;
using OpenStore.Domain.Catalog;

namespace OpenStore.Api.Endpoints.Admin;

/// <summary>
/// 后台商品端点。<b>每个端点都必须挂具体权限点</b>（门禁 Category=Authorization 逐个扫描本文件，
/// 出现裸 <c>.RequireAuthorization()</c> 或完全没有授权即判红）。
/// </summary>
public static class ProductAdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/products")
            .WithTags("Admin.Catalog");

        // GET /api/admin/products —— 列表（分页 / 状态筛选 / 关键词 / 排序）
        group.MapGet("", async (
            string? q,
            ProductStatus? status,
            string? sort,
            int? page,
            int? pageSize,
            AdminProductQueryService query,
            CancellationToken ct) =>
        {
            var filter = new AdminProductListFilter(q, status, sort, page ?? 1, pageSize ?? AdminProductQueryService.DefaultPageSize);
            var result = await query.ListAsync(filter, ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("AdminListProducts")
        .RequireAuthorization(Permissions.ProductRead)
        .Produces<AdminProductListResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET /api/admin/products/{id} —— 详情（含变体）
        group.MapGet("/{id:guid}", async (
            Guid id,
            ProductAdminService service,
            CancellationToken ct) =>
        {
            var result = await service.GetDetailAsync(id, ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("AdminGetProduct")
        .RequireAuthorization(Permissions.ProductRead)
        .Produces<AdminProductDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/admin/products —— 新建（slug + title + 变体含价格）
        group.MapPost("", async (
            CreateProductRequest request,
            ProductAdminService service,
            CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct).ConfigureAwait(false);
            return Results.Created($"/api/admin/products/{result.Id}", result);
        })
        .WithName("AdminCreateProduct")
        .RequireAuthorization(Permissions.ProductCreate)
        .Produces<AdminProductDetailDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // PATCH /api/admin/products/{id} —— 编辑（title / 变体 SKU / 变体价格）
        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateProductRequest request,
            ProductAdminService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, request, ct).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("AdminUpdateProduct")
        .RequireAuthorization(Permissions.ProductEdit)
        .Produces<AdminProductDetailDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PATCH /api/admin/products/{id}/status —— 状态变更（上架/下架）
        group.MapPatch("/{id:guid}/status", async (
            Guid id,
            ChangeProductStatusRequest request,
            ProductAdminService service,
            CancellationToken ct) =>
        {
            var result = await service.ChangeStatusAsync(id, request.Status, ct).ConfigureAwait(false);

            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("AdminChangeProductStatus")
        .RequireAuthorization(Permissions.ProductWrite)
        .Produces<AdminProductStatusDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
