using OpenStore.Application.Abstractions;
using OpenStore.Application.Common;
using OpenStore.Application.Inventory;
using OpenStore.Domain.Inventory;
using OpenStore.Domain.Shared;

namespace OpenStore.Api.Endpoints.Admin;

/// <summary>
/// 后台库存端点（契约 <c>openapi/openstore.yaml</c> 的 <c>Admin.Inventory</c> 分组）。
/// </summary>
/// <remarks>
/// <para>
/// <b>每个端点单独挂具体权限点</b>（门禁 Category=Authorization 逐个扫本文件）：
/// 库存的"读"与"调"是两个权限点，`inventory.adjust` 是会被审计追责的写操作。
/// </para>
/// <para>
/// <b>本文件不做业务判断</b>：
/// <list type="bullet">
/// <item>"新值小于 Reserved+Sold 不能调"在聚合 <see cref="StockItem.AdjustOnHand"/> 里，
/// 端点不写 <c>if (newOnHand &lt; 0) throw</c> —— 规则写两遍就一定会有一遍写得不一样；</item>
/// <item>并发保护在仓储的条件 UPDATE（影响 0 行 → <c>CONCURRENT_UPDATE</c> → 409），
/// 端点不做重试：重试属于用例层，藏在端点里会变成"看不见的覆盖"。</item>
/// </list>
/// </para>
/// </remarks>
public static class InventoryAdminEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/inventory")
            .WithTags("Admin.Inventory");

        // GET /api/admin/inventory —— 库存列表（分页 / 关键词 / 只看低库存）
        group.MapGet("", async (
            string? q,
            bool? lowStockOnly,
            int? page,
            int? pageSize,
            IStockRepository stocks,
            CancellationToken ct) =>
        {
            var paging = ToPageRequest(page, pageSize);
            var filter = new StockListFilter(q, lowStockOnly ?? false);

            var result = await stocks.ListAsync(filter, paging, ct).ConfigureAwait(false);

            return Results.Ok(result);
        })
        .WithName("AdminListInventory")
        .RequireAuthorization(Permissions.InventoryRead)
        .Produces<PagedResult<StockOverviewDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/admin/inventory/{variantId}/adjust —— 手工调整在库数量（必填原因）
        group.MapPost("/{variantId:guid}/adjust", async (
            Guid variantId,
            AdjustInventoryRequest request,
            IStockRepository stocks,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            var item = await stocks.GetAsync(variantId, ct).ConfigureAwait(false);

            if (item is null)
            {
                // 变体存在但没有库存行 = 数据不一致。这里不"顺手补建"：
                // 补建会把不一致掩盖成一次静默的期初入库，之后没人知道这行是怎么冒出来的。
                throw new DomainException(
                    "CATALOG_NOT_FOUND",
                    $"Stock row for variant '{variantId}' does not exist; cannot adjust.");
            }

            // 操作者只从 ICurrentUser 取（绝不从请求体取）：否则任何人都能伪造成别人的调整
            var operatorId = currentUser.UserId
                ?? throw new DomainException("AUTH_SESSION_EXPIRED", "Adjusting inventory requires an authenticated admin.");

            var @operator = StockOperator.Admin(operatorId, currentUser.DisplayName ?? "admin");

            // 业务规则（下限 = Reserved + Sold、原因必填、负库存不可表示）全部在聚合里校验
            item.AdjustOnHand(request.NewOnHand, request.Reason, @operator);

            // 先看聚合有没有产生流水：delta = 0 时聚合直接返回（幂等），此时不应去查"最近一条流水"冒充本次结果
            var wroteMovement = item.PendingMovements.Count > 0;

            await stocks.SaveAsync(item, ct).ConfigureAwait(false);

            long? movementId = null;
            if (wroteMovement)
            {
                // 流水号由自增主键给出，仓储的保存接口不回传它（端口签名不为此扩张）。
                // 取该变体最新一条即本次写入的那条：Id 自增即时间序，且刚提交，不会被后写者插队。
                var latest = await stocks.ListMovementsAsync(variantId, new PageRequest(1, 1), ct).ConfigureAwait(false);
                movementId = latest.Items.Count > 0 ? latest.Items[0].Id : null;
            }

            return Results.Ok(new AdjustInventoryResponse(item.OnHand, item.Available, movementId));
        })
        .WithName("AdminAdjustInventory")
        .RequireAuthorization(Permissions.InventoryAdjust)
        .Produces<AdjustInventoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // GET /api/admin/inventory/{variantId}/movements —— 库存流水（只追加表，只读）
        group.MapGet("/{variantId:guid}/movements", async (
            Guid variantId,
            int? page,
            int? pageSize,
            IStockRepository stocks,
            CancellationToken ct) =>
        {
            var paging = ToPageRequest(page, pageSize);

            var result = await stocks.ListMovementsAsync(variantId, paging, ct).ConfigureAwait(false);

            return Results.Ok(result);
        })
        .WithName("AdminListInventoryMovements")
        .RequireAuthorization(Permissions.InventoryRead)
        .Produces<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// 分页参数校验（§5.1：<c>page ∈ [1,100]</c>、<c>pageSize ∈ [1,100]</c>，缺省 24）。
    /// </summary>
    /// <remarks>
    /// 越界抛 <see cref="DomainException"/> 而不是返回 <c>Results.BadRequest</c>：
    /// 错误必须从中间件一个出口出，否则同一类错误会有两种响应形状（前端要写两套解析）。
    /// 注意 <c>page</c> 合法但超过总页数<b>不是</b>错误 —— 翻过头是用户行为，返回空页即可。
    /// </remarks>
    private static PageRequest ToPageRequest(int? page, int? pageSize)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? PageRequest.DefaultPageSize;

        if (resolvedPage < 1 || resolvedPage > PageRequest.MaxPage)
        {
            throw new DomainException(
                "PAGE_OUT_OF_RANGE",
                $"page must be within [1, {PageRequest.MaxPage}], got {resolvedPage}.");
        }

        if (resolvedPageSize < 1 || resolvedPageSize > PageRequest.MaxPageSize)
        {
            throw new DomainException(
                "PAGE_SIZE_EXCEEDED",
                $"pageSize must be within [1, {PageRequest.MaxPageSize}], got {resolvedPageSize}.");
        }

        return new PageRequest(resolvedPage, resolvedPageSize);
    }
}
