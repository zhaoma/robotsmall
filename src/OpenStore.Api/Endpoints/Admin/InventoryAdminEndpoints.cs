using System.Globalization;
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

            var items = result.Items.Select(ToMovementResponse).ToList();

            return Results.Ok(new PagedResult<InventoryMovementResponse>(
                items,
                result.Page,
                result.PageSize,
                result.Total));
        })
        .WithName("AdminListInventoryMovements")
        .RequireAuthorization(Permissions.InventoryRead)
        .Produces<PagedResult<InventoryMovementResponse>>(StatusCodes.Status200OK)
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

    /// <summary>
    /// 把仓储 DTO 映射成契约 <c>InventoryMovement</c> 的形状。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么要在端点里再映射一次、不直接返回 DTO</b>：DTO 是按"仓储需要什么"设计的
    /// （三对 delta + 三对 after 快照），而契约是按"管理员怎么读"设计的
    /// （<c>operator</c> 对象、<c>delta</c> 与 <c>onHandBefore</c> 互为校验、id 是不透明字符串）。
    /// 直接直出 DTO 就是让持久层的数据形状决定外部契约，字段名一改前端就断。
    /// </para>
    /// <para>
    /// <b><c>onHandBefore</c> 由 <c>after − delta</c> 反算而不是加一列</b>：
    /// 存一列 before 就多一个可能与 delta/after 不一致的真源（三条数据必须同时正确才算对账通过）。
    /// 反算只有一个真源，不变量 <c>before + delta = after</c> 恒成立。
    /// </para>
    /// <para>
    /// <b><c>orderNumber</c> / <c>holdId</c> 恒 <c>null</c></b>：M1 没有订单上下文（只有 <c>OrderId</c>），
    /// 也没有预占凭据实体。按主理人裁决"严格按契约补齐、缺的留 null"上架，
    /// 等 M2 订单落地后填真值。<b>不允许</b>在这里用 OrderId 拼一个假单号充数——前端会拿它去查单。
    /// </para>
    /// </remarks>
    private static InventoryMovementResponse ToMovementResponse(StockMovementDto movement)
        => new(
            // 契约把 id 定成不透明字符串（前端不做算术、不做 +1 翻页），因此这里显式转字符串
            Id: movement.Id.ToString(CultureInfo.InvariantCulture),
            OccurredAt: movement.OccurredAt,
            ChangeType: movement.ChangeType,
            Operator: new InventoryMovementOperatorResponse(
                movement.OperatorType,
                movement.OperatorId?.ToString(),
                movement.OperatorName),
            OnHandBefore: movement.OnHandAfter - movement.OnHandDelta,
            OnHandAfter: movement.OnHandAfter,
            Delta: movement.OnHandDelta,
            ReservedAfter: movement.ReservedAfter,
            SoldAfter: movement.SoldAfter,
            Reason: movement.Reason,
            OrderNumber: null,
            HoldId: null);
}

/// <summary>契约 <c>InventoryMovement</c>。<b>字段名与 openapi 逐字对齐</b>，多一个少一个都算漂移。</summary>
/// <param name="Id">流水主键（字符串：不透明标识，前端不参与算术）。</param>
/// <param name="OccurredAt">发生时间（UTC）。</param>
/// <param name="ChangeType">变化类型（只追加不可改）。</param>
/// <param name="Operator">操作人快照（人员改名后流水仍还原当时身份）。</param>
/// <param name="OnHandBefore">变化前在库数（由 <c>after − delta</c> 反算）。</param>
/// <param name="OnHandAfter">变化后在库数。</param>
/// <param name="Delta">在库数变化量（有符号）。</param>
/// <param name="ReservedAfter">预占数后置快照（与 delta 互为校验）。</param>
/// <param name="SoldAfter">已售数后置快照。</param>
/// <param name="Reason">手工调整原因（US-E2-08 要求必填）。</param>
/// <param name="OrderNumber">关联订单号；M1 无订单上下文，恒 <c>null</c>。</param>
/// <param name="HoldId">预占凭据；M1 无常驻预占实体，恒 <c>null</c>。</param>
internal sealed record InventoryMovementResponse(
    string Id,
    DateTimeOffset OccurredAt,
    StockChangeType ChangeType,
    InventoryMovementOperatorResponse Operator,
    int OnHandBefore,
    int OnHandAfter,
    int Delta,
    int ReservedAfter,
    int SoldAfter,
    string? Reason,
    string? OrderNumber,
    string? HoldId);

/// <summary>契约里 <c>InventoryMovement.operator</c> 的内联对象。</summary>
/// <param name="Kind">操作人类型：System / Admin / Customer。</param>
/// <param name="Id">操作人主键；系统操作时为 <c>null</c>。</param>
/// <param name="Name">操作人名称快照。</param>
internal sealed record InventoryMovementOperatorResponse(
    StockOperatorType Kind,
    string? Id,
    string? Name);
