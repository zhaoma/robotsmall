using Microsoft.EntityFrameworkCore;
using OpenStore.Infrastructure.Persistence;

namespace OpenStore.Api.Endpoints;

/// <summary>
/// 存活/就绪探针（契约 <c>#/paths/~1health~1live</c> 与 <c>~1health~1ready</c>；部署文档 §6）。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个探针的职责严格分开，混用会放大故障</b>：
/// <list type="bullet">
/// <item><c>/health/live</c>：只证明"进程还能响应"，<b>不碰数据库</b>。编排用它决定"要不要重启容器"；
/// 若它也查库，一次 DB 慢查询就会让所有实例被判定为死、被批量重启 —— 把一次抖动放大成全站 502。</item>
/// <item><c>/health/ready</c>：判断"能不能接流量"，查 DB / 待执行迁移 / 媒体目录 / 模板目录。
/// 编排用它决定"要不要把这个实例从负载均衡里摘掉"。</item>
/// </list>
/// </para>
/// <para>
/// <b>就绪检查不看"是否已完成安装"</b>：全新部署未初始化也应该是 ready，
/// 否则首次部署永远起不来（向导页都打不开，因为反代把请求挡在 503 上）。
/// </para>
/// <para>
/// <b>失败返回 503 + 明细，不返回 200 + 空对象</b>：反代的可用性告警靠状态码触发，
/// 而"200 的降级"会让告警永远不响 —— 这正是自托管产品最常见的"静默不可用"。
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- 存活：不查任何外部资源 ----
        app.MapGet("/health/live", () => Results.Ok(new LiveResponse("live")))
            .WithName("HealthLive")
            .AllowAnonymous()
            .Produces<LiveResponse>(StatusCodes.Status200OK);

        // ---- 就绪：四项检查 ----
        app.MapGet("/health/ready", async (
            StoreDbContext db,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Health.Ready");

            var dbCheck = await CheckDatabaseAsync(db, logger, ct).ConfigureAwait(false);
            var migrationsCheck = await CheckMigrationsAsync(db, logger, ct).ConfigureAwait(false);
            var mediaCheck = CheckDirectory(configuration["Storage:MediaRoot"], "media", logger);
            var templatesCheck = CheckDirectory(configuration["Storage:ThemesRoot"] ?? "themes", "templates", logger);

            var ready = dbCheck.IsUp && migrationsCheck.IsUp && mediaCheck.IsUp && templatesCheck.IsUp;

            var response = new ReadyResponse(
                ready ? "ready" : "degraded",
                new ReadyChecks(dbCheck, migrationsCheck, mediaCheck, templatesCheck),
                DateTimeOffset.UtcNow);

            if (ready)
            {
                return Results.Ok(response);
            }

            // 503 + Retry-After：让编排与反代知道"等一会儿再试"，而不是当成永久故障
            return new RetryAfterResult(
                Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable),
                seconds: 30);
        })
        .WithName("HealthReady")
        .AllowAnonymous()
        .Produces<ReadyResponse>(StatusCodes.Status200OK)
        .Produces<ReadyResponse>(StatusCodes.Status503ServiceUnavailable);

        // ---- 兼容 M0 的旧路径：部署脚本与冒烟脚本已经依赖 {"status":"ok"} ----
        app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok")))
            .WithName("Health")
            .AllowAnonymous()
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>DB 可用性：只做一次 <c>CanConnectAsync</c>，不跑任何查询。</summary>
    /// <remarks>
    /// 失败只记 Warning 不抛：探针本身不能因为"被探的对象坏了"而 500 —— 那样编排拿不到明细，
    /// 只能看到"探针也挂了"，排障时反而少一条线索。
    /// </remarks>
    private static async Task<CheckResult> CheckDatabaseAsync(StoreDbContext db, ILogger logger, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var ok = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
            return Result(ok, elapsed(started), ok ? null : "数据库不可连接（检查 ConnectionStrings:Default 与文件权限）");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "health.ready.db.failed");
            return Result(false, elapsed(started), ex.GetType().Name);
        }
    }

    /// <summary>待执行迁移数 &gt; 0 → down：滚动发布期间新实例不该先接流量（它读到的表结构比代码旧）。</summary>
    private static async Task<CheckResult> CheckMigrationsAsync(StoreDbContext db, ILogger logger, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToList();
            return Result(
                pending.Count == 0,
                elapsed(started),
                pending.Count == 0 ? null : $"有 {pending.Count} 个迁移未执行：{string.Join(", ", pending.Take(3))}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "health.ready.migrations.failed");
            return Result(false, elapsed(started), ex.GetType().Name);
        }
    }

    /// <summary>目录可读可写检查。未配置时按默认相对目录检查（自托管默认就是这么跑的）。</summary>
    private static CheckResult CheckDirectory(string? path, string label, ILogger logger)
    {
        var started = DateTimeOffset.UtcNow;
        var resolved = string.IsNullOrWhiteSpace(path) ? (label == "media" ? "./data/media" : "./themes") : path;

        try
        {
            if (!Directory.Exists(resolved))
            {
                // 目录不存在不等于故障：全新自托管部署首次启动时 ./data/media 还没建。
                // 这里顺手创建（应用自有存储目录，不是用户数据），创建失败才算 down ——
                // 若把"目录不存在"直接报成 down，全新部署的就绪探针永远是 503，
                // 反代会一直不转发流量，表现为"装好了但打不开"（最容易误判成镜像问题）。
                Directory.CreateDirectory(resolved);
            }

            // 可写性只能靠"真的写一次"验证（不是权限位推断）。探针文件用固定名、写完立刻删：
            // 固定名避免目录里堆一堆临时文件，删除避免容器磁盘被探针写满。
            var probePath = Path.Combine(resolved, ".health-probe");
            try
            {
                using (var handle = new FileStream(probePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    handle.WriteByte(0);
                }
            }
            finally
            {
                if (File.Exists(probePath))
                {
                    try
                    {
                        File.Delete(probePath);
                    }
                    catch (IOException)
                    {
                        // 删不掉不算不就绪：可能另一个探针正在写同名文件。下次覆盖即可。
                    }
                }
            }

            return Result(true, elapsed(started), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "health.ready.{Label}.failed", label);
            return Result(false, elapsed(started), ex.GetType().Name);
        }
    }

    private static CheckResult Result(bool ok, long durationMs, string? detail)
        => new(ok ? "up" : "down", detail, durationMs);

    private static long elapsed(DateTimeOffset started)
        => (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

    /// <summary>存活响应（契约 <c>{"status":"live"}</c>）。</summary>
    public sealed record LiveResponse(string Status);

    /// <summary>就绪响应（契约 <c>{status, checks, checkedAt}</c>）。</summary>
    public sealed record ReadyResponse(string Status, ReadyChecks Checks, DateTimeOffset CheckedAt);

    public sealed record ReadyChecks(CheckResult Db, CheckResult Migrations, CheckResult MediaDir, CheckResult TemplatesDir);

    /// <summary>单项检查结果。用 <c>Status</c> 字符串而不是 bool：契约里就是 <c>up</c>/<c>down</c>。</summary>
    public sealed record CheckResult(string Status, string? Detail, long DurationMs)
    {
        /// <summary>是否通过。<c>up</c> 之外的一切都算不通过（未知状态不放过）。</summary>
        /// <remarks>不序列化：契约的 check 项只有 <c>status/detail/durationMs</c>，多一个 <c>isUp</c> 是噪音。</remarks>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsUp => string.Equals(Status, "up", StringComparison.Ordinal);
    }

    /// <summary>兼容 M0：响应体形状固定为 <c>{"status":"ok"}</c>（部署健康检查与冒烟脚本依赖它）。</summary>
    public sealed record HealthResponse(string Status);

    /// <summary>给 503 响应补 <c>Retry-After</c> 的轻量包装（Minimal API 的 <c>Results.Json</c> 不支持直接加头）。</summary>
    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
