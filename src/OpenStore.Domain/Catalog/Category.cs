using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 分类聚合根：<b>邻接表（<see cref="ParentId"/>） + 物化路径（<see cref="Path"/>）</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么两者都要，而不是只留一个</b>（数据模型 §3.1 Category 的取舍表）：
/// 邻接表是<b>写</b>的最优形态（改父节点只改一行），物化路径是<b>读</b>的最优形态
/// （分类页要"含子树全部商品"，<c>Path LIKE '/A/%'</c> 一条单表前缀查询就取到子树 id 集，
/// 索引 <c>IX_Category_Path</c> 正是服务它）。不要建 <c>CategoryClosure</c>：分类总量在百级，
/// 闭包表的写放大（移动节点改 O(n) 行）与额外表/触发器不值得。
/// </para>
/// <para>
/// <b><see cref="Path"/> 的形态与 <see cref="Depth"/> 的定义</b>：路径形如 <c>/{Id}/{Id}/…/</c>
/// （<b>首尾都带斜杠</b>），<see cref="Depth"/> = 路径段数，因此<b>根分类 Depth = 1</b>、
/// 最深 5 级（<see cref="MaxDepth"/>）。这条口径来自数据模型 §3.1 的 <c>Path</c> 样例
/// <c>/1/7/23/</c>（三段 = 三级）与 <c>CHECK (Depth &gt;= 0 AND Depth &lt;= 5)</c>：
/// CHECK 的下界 0 是<b>防御性</b>的（不存在合法的 0 级分类），不放宽到"允许 0 级"。
/// 与基准数据生成器的分类骨架（<c>BenchDataSeeder.Shape</c>：2 根 Depth=1、叶 Depth=3）一致。
/// </para>
/// <para>
/// <b>不变量（本类负责）</b>：路径必须以自身 <see cref="Id"/> 结尾且受父路径前缀约束、
/// <see cref="Depth"/> 必须等于路径段数、深度不超过 <see cref="MaxDepth"/>、移动节点不得形成环。
/// <b>不变量（本类不负责，需要跨表计数）</b>：有子分类或有商品时禁止删除（数据模型 §1.3
/// 的"条件硬删"），由用例层查 <c>Category</c>/<c>ProductCategory</c> 后决定。
/// </para>
/// </remarks>
public sealed class Category
{
    /// <summary>深度硬上限（数据模型 §3.1：需求至少 3 级，上限 5 防失控）。与 <c>CK_Category_Depth</c> 同口径。</summary>
    public const int MaxDepth = 5;

    private Category(
        Guid id,
        Guid? parentId,
        string path,
        int sortOrder,
        CategoryStatus status,
        Guid? attributeSetId,
        bool isDeleted,
        DateTime? deletedAtUtc,
        long version)
    {
        if (id == Guid.Empty)
        {
            throw new InvariantViolationException("CATEGORY_ID_REQUIRED", "Category id must not be empty.");
        }

        Id = id;
        ParentId = parentId;
        Path = GuardPath(path, id, parentId);
        Depth = DepthOf(Path);
        SortOrder = sortOrder;
        Status = GuardStatus(status);
        AttributeSetId = attributeSetId;
        IsDeleted = isDeleted;
        DeletedAt = deletedAtUtc;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>父分类；<c>null</c> = 根分类。</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>物化路径 <c>/{Id}/{Id}/…/</c>。子树查询用它做前缀匹配，不要手工拼。</summary>
    public string Path { get; private set; }

    /// <summary>层级数 = <see cref="Path"/> 的段数（根 = 1）。DB 侧有 <c>CK_Category_Depth</c>。</summary>
    public int Depth { get; private set; }

    public int SortOrder { get; private set; }

    public CategoryStatus Status { get; private set; }

    /// <summary>
    /// 生效属性集；<c>null</c> = 继承父链（数据模型 §3.1）。
    /// </summary>
    /// <remarks>
    /// M1 第一批表只建 <c>Category</c>，<c>AttributeSet</c> 表在属性批次落地，
    /// 因此本列<b>先建列、暂不建外键</b>（详见 <c>CategoryConfiguration</c> 的注释）。
    /// 在本列有写入方之前它恒为 <c>null</c>，语义就是"继承父链"，不会误导。
    /// </remarks>
    public Guid? AttributeSetId { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>软删时间（UTC）。仅审计与保留策略使用，不参与任何查询过滤之外的判断。</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>乐观锁版本号（数据模型 §1.4：<see cref="Category"/> 属"需要乐观锁"的表）。</summary>
    public long Version { get; private set; }

    public bool IsRoot => ParentId is null;

    /// <summary>能否出现在前台的导航树 / 分类页：未软删且不是 Hidden。</summary>
    public bool IsVisible => !IsDeleted && Status == CategoryStatus.Active;

    /// <summary>新建根分类（Depth=1、Path=<c>/{id}/</c>）。</summary>
    public static Category CreateRoot(
        Guid id,
        int sortOrder = 0,
        CategoryStatus status = CategoryStatus.Active,
        Guid? attributeSetId = null)
        => new(id, parentId: null, RootPath(id), sortOrder, status, attributeSetId, false, null, version: 1);

    /// <summary>
    /// 在给定父分类下新建子分类。父级已到 <see cref="MaxDepth"/> 时抛 <c>CATEGORY_DEPTH_EXCEEDED</c>。
    /// </summary>
    public static Category CreateChild(
        Guid id,
        Category parent,
        int sortOrder = 0,
        CategoryStatus status = CategoryStatus.Active,
        Guid? attributeSetId = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (parent.Depth >= MaxDepth)
        {
            throw new InvariantViolationException(
                "CATEGORY_DEPTH_EXCEEDED",
                $"Parent category '{parent.Id}' is already at depth {parent.Depth} (max {MaxDepth}); " +
                "a child would exceed the depth limit.");
        }

        return new(id, parent.Id, ChildPath(parent.Path, id), sortOrder, status, attributeSetId, false, null, version: 1);
    }

    /// <summary>
    /// 从持久化层重建。与 <see cref="CreateRoot"/> 的区别只是允许指定已有状态，
    /// 但仍然<b>完整校验</b>路径与深度：读出来的数据违反规则时宁可炸在读取侧，
    /// 也不要把脏分类流到导航树与分类页上。
    /// </summary>
    public static Category Rehydrate(
        Guid id,
        Guid? parentId,
        string path,
        int sortOrder,
        CategoryStatus status,
        Guid? attributeSetId,
        bool isDeleted,
        DateTime? deletedAtUtc,
        long version)
        => new(id, parentId, path, sortOrder, status, attributeSetId, isDeleted, deletedAtUtc, version);

    /// <summary>
    /// 移动/改序。返回新路径；<b>调用方必须随后重建该节点整棵子树的路径</b>
    /// （子树的 <see cref="Path"/> 前缀依赖父路径，单改一行会让子树查询静默漏掉整棵子树）。
    /// </summary>
    /// <remarks>
    /// 环检测用路径前缀而不是递归查父链：路径前缀是 O(1) 且与"子孙关系"定义完全等价
    /// （<c>Path</c> 由 id 链拼成，前缀相等 ⟺ 祖先关系）。<see cref="MaxDepth"/> 也要按
    /// "新父级深度 + 本子树高度"重算，而子树高度只有调用方知道 → 因此这里只校验单节点，
    /// 深度溢出由 <c>CATEGORY_SUBTREE_DEPTH_EXCEEDED</c> 在使用例层抛（见迁移/域服务注释）。
    /// </remarks>
    public void MoveTo(Category? newParent, int sortOrder)
    {
        if (newParent is null && IsRoot && sortOrder == SortOrder)
        {
            return; // 幂等：根分类原地改序不涉及路径变化
        }

        if (newParent is not null)
        {
            if (newParent.Id == Id)
            {
                throw new InvariantViolationException(
                    "CATEGORY_CANNOT_PARENT_SELF",
                    $"Category '{Id}' cannot be its own parent.");
            }

            // 目标父级在自己子树内 → 环。StartsWith 用的是 Ordinal：路径只含 '/'、十六进制与 '-'，
            // 与区域性无关，用非 Ordinal 比较反而会让同一份数据在土耳其语等 locale 上算出不同结果。
            if (newParent.Path.StartsWith(Path, StringComparison.Ordinal))
            {
                throw new InvariantViolationException(
                    "CATEGORY_MOVE_WOULD_CYCLE",
                    $"Cannot move category '{Id}' under '{newParent.Id}': the target is inside its own subtree.");
            }
        }

        ParentId = newParent?.Id;
        Path = newParent is null ? RootPath(Id) : ChildPath(newParent.Path, Id);
        Depth = DepthOf(Path);

        if (Depth > MaxDepth)
        {
            throw new InvariantViolationException(
                "CATEGORY_DEPTH_EXCEEDED",
                $"Category '{Id}' would land at depth {Depth} (max {MaxDepth}).");
        }

        SortOrder = sortOrder;
    }

    /// <summary>改同层排序（拖拽排序的单节点版本，不动路径）。</summary>
    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>切换可见状态。非法枚举值（请求体写 99）在聚合边界拦住，否则会以脏数据落库。</summary>
    public void ChangeStatus(CategoryStatus target)
    {
        Status = GuardStatus(target);
    }

    /// <summary>设置生效属性集；<c>null</c> = 回到"继承父链"。</summary>
    public void SetAttributeSet(Guid? attributeSetId)
    {
        AttributeSetId = attributeSetId;
    }

    /// <summary>
    /// 软删。**不触碰 <c>ProductCategory</c>**：归档/隐藏/删除分类都不改变商品归属
    /// （数据模型 §3.1：「归档不改变这两条」——"替代分类页"的判定来源就是删除<b>前</b>所属分类）。
    /// </summary>
    public void MarkDeleted(DateTime deletedAtUtc)
    {
        if (IsDeleted)
        {
            return; // 幂等
        }

        if (deletedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvariantViolationException(
                "CATEGORY_DELETED_AT_NOT_UTC",
                "DeletedAt must be DateTimeKind.Utc (数据模型 §1.2 禁用本地时间)。");
        }

        IsDeleted = true;
        DeletedAt = deletedAtUtc;
    }

    /// <summary>恢复软删（管理端"回收站"路径）。</summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
    }

    /// <summary>本节点的子树前缀，直接用于 <c>Path LIKE {SubtreePattern}</c>。</summary>
    public string SubtreePattern => Path + "%";

    /// <summary>根分类路径：<c>/{id}/</c>。</summary>
    public static string RootPath(Guid id) => "/" + id.ToString("D") + "/";

    /// <summary>子分类路径：<c>{父路径}{id}/</c>（父路径已带尾斜杠，不重复补）。</summary>
    public static string ChildPath(string parentPath, Guid id)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            throw new InvariantViolationException(
                "CATEGORY_PARENT_PATH_REQUIRED",
                "Parent path must not be empty; build it with RootPath/ChildPath.");
        }

        return parentPath + id.ToString("D") + "/";
    }

    /// <summary>路径段数（= 深度）。用于校验与重建，避免 <see cref="Depth"/> 与 <see cref="Path"/> 两处各说一套。</summary>
    public static int DepthOf(string path)
    {
        var body = path.Trim('/');
        if (body.Length == 0)
        {
            return 0;
        }

        var segments = 0;
        foreach (var _ in body.Split('/'))
        {
            segments++;
        }

        return segments;
    }

    private static string GuardPath(string path, Guid id, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvariantViolationException(
                "CATEGORY_PATH_REQUIRED",
                "Category path is required; build it with RootPath/ChildPath.");
        }

        if (path.Length > 400)
        {
            throw new InvariantViolationException(
                "CATEGORY_PATH_TOO_LONG",
                $"Category path must be <= 400 chars (column limit), got {path.Length}.");
        }

        if (!path.StartsWith('/') || !path.EndsWith('/'))
        {
            throw new InvariantViolationException(
                "CATEGORY_PATH_MALFORMED",
                $"Category path '{path}' must start and end with '/' (物化路径形如 /{{id}}/{{id}}/)。");
        }

        // 路径必须"以自身 Id 结尾"：否则 Path 与 Id 指向两个节点，子树查询会命中别人的子树。
        var expectedTail = id.ToString("D") + "/";
        if (!path.EndsWith(expectedTail, StringComparison.Ordinal))
        {
            throw new InvariantViolationException(
                "CATEGORY_PATH_TAIL_MISMATCH",
                $"Category path '{path}' must end with its own id ('{expectedTail}').");
        }

        // 根分类的路径只有一个段；有父级时必须至少两段（父 + 自己）。
        var depth = DepthOf(path);
        if (parentId is null && depth != 1)
        {
            throw new InvariantViolationException(
                "CATEGORY_ROOT_PATH_INVALID",
                $"Root category '{id}' must have exactly one path segment, got {depth} ('{path}').");
        }

        if (parentId is not null && depth < 2)
        {
            throw new InvariantViolationException(
                "CATEGORY_CHILD_PATH_INVALID",
                $"Child category '{id}' must have at least two path segments, got {depth} ('{path}').");
        }

        if (depth > MaxDepth)
        {
            throw new InvariantViolationException(
                "CATEGORY_DEPTH_EXCEEDED",
                $"Category '{id}' path depth {depth} exceeds the limit {MaxDepth}.");
        }

        return path;
    }

    private static CategoryStatus GuardStatus(CategoryStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new InvariantViolationException(
                "CATEGORY_STATUS_INVALID",
                $"'{status}' is not a defined CategoryStatus.");
        }

        return status;
    }
}
