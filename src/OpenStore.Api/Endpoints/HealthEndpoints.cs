namespace OpenStore.Api.Endpoints;

/// <summary>
/// 存活/就绪探针。容器编排与负载均衡用它判断"进程能不能接流量"（部署文档 §6）。
/// </summary>
/// <remarks>
/// 必须<b>不依赖任何外部资源</b>：健康检查去 ping 数据库，数据库一抖动就会把所有实例摘掉，
/// 把一次 DB 慢查询放大成一次全站 502。DB 就绪性另有 <c>/api/health/ready</c>（M1）。
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")))
            .WithName("Health")
            .AllowAnonymous()
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>响应体形状固定为 <c>{"status":"ok"}</c>（部署健康检查与冒烟脚本依赖它）。</summary>
    public sealed record HealthResponse(string Status);
}
