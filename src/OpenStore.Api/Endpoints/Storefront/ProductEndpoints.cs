using OpenStore.Application.Catalog.Dtos.Storefront;
using OpenStore.Application.Catalog.Queries;

namespace OpenStore.Api.Endpoints.Storefront;

/// <summary>
/// 门店商品端点（公开，无需认证）。
/// </summary>
/// <remarks>
/// <b>路由必须带 <c>{locale}</c> 前缀</b>（架构 §0 多语种裁定）：路径唯一 → 缓存键唯一 →
/// 无重复内容（AC-11）。locale 用正则约束成两位小写字母，避免 <c>/api/v1/products/x</c>
/// 这种旧形态被误当成 locale="products" 而落入 404 语义混乱。
/// </remarks>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapStorefrontProductEndpoints(this IEndpointRouteBuilder app)
    {
        // 注意 {locale:regex(^[a-z]{{2}}$)} 里的双花括号：路由模板用 {{ }} 转义字面量花括号
        var group = app.MapGroup("/api/v1/{locale:regex(^[a-z]{{2}}$)}")
            .WithTags("Storefront.Catalog");

        group.MapGet("/products/{slug}", async (
            string locale,
            string slug,
            StorefrontProductQueryService service,
            CancellationToken ct) =>
        {
            // locale 参与翻译/币种解析（M1）；M0 只做路由合法性校验，不参与查询
            _ = locale;

            var dto = await service.GetBySlugAsync(slug, ct).ConfigureAwait(false);

            // 不存在 / 未上架 / 无价格统一 404：不给爬虫任何"这个商品存在但你不能看"的信号
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("StorefrontGetProductBySlug")
        .AllowAnonymous()
        .Produces<StorefrontProductDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
