using OpenStore.Domain.Shared;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 商品↔分类 的多对多绑定，并标记<b>主分类</b>（复合主键 <c>(ProductId, CategoryId)</c>，无独立 Id）。
/// </summary>
/// <remarks>
/// <para>
/// <b>"主分类"是两半，且由两个不同的地方保证</b>（数据模型 §3.1 那张两行表）：
/// </para>
/// <list type="table">
/// <item>
/// <term>至多一个</term>
/// <description>
/// <b>DB</b>：部分唯一索引 <c>UX_ProductCategory_PrimaryPerProduct</c>（<c>(ProductId) WHERE IsPrimary</c>）。
/// 两个 primary ⇒ 面包屑与 canonical 有两个"都合法"的答案，而歧义不会报错，只会在某次查询计划
/// 变化时换一个——正是本项目反复在治的"两个真源"。
/// </description>
/// </item>
/// <item>
/// <term>至少一个</term>
/// <description>
/// <b>领域/装载校验</b>：索引只能拦"多"，拦不了"缺"。缺 primary 的后果是面包屑退化成空、
/// canonical 掉到分类页——表现为"页面能开但没有面包屑"，很容易被当成样式问题。
/// 落点就是本类的 <see cref="EnsureExactlyOnePrimaryPerProduct"/>。
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>为什么这条校验必须做成可被批量路径复用的静态方法</b>：领域约束只在"走聚合"的那条路上生效，
/// 而 <c>fixture</c> 装载、基准数据生成、CSV 导入都是<b>批量直写</b>——它们不经过聚合，
/// 所以"至少一个 primary"在那些路径上没有任何一层会拦。更糟的是 <see cref="IsPrimary"/> 的列
/// 默认值是 <c>false</c>，所以<b>"忘了设"是默认结果，且完全静默</b>（写成"全部设 true"反而会撞
/// 部分唯一索引当场报错——错得响的那一面不用担心）。因此判据只有一条：
/// <b>任何批量路径，每商品恰好一行 IsPrimary = true</b>，缺了要在装载阶段报。
/// </para>
/// <para>
/// <b>归档不清绑定</b>：<c>Product.Status = Archived</c> 不得删除、也不得清空本表（含
/// <c>IsPrimary</c> 行）——AC-11"替代分类页"的判定来源正是"归档<b>前</b>所属分类"，
/// 清掉它 301 分支永远走不到。
/// </para>
/// </remarks>
public sealed class ProductCategory
{
    private ProductCategory(Guid productId, Guid categoryId, bool isPrimary, int sortOrder)
    {
        if (productId == Guid.Empty)
        {
            throw new InvariantViolationException(
                "PRODUCT_CATEGORY_PRODUCT_REQUIRED",
                "ProductCategory.ProductId must not be empty.");
        }

        if (categoryId == Guid.Empty)
        {
            throw new InvariantViolationException(
                "PRODUCT_CATEGORY_CATEGORY_REQUIRED",
                "ProductCategory.CategoryId must not be empty.");
        }

        ProductId = productId;
        CategoryId = categoryId;
        IsPrimary = isPrimary;
        SortOrder = sortOrder;
    }

    public Guid ProductId { get; }

    public Guid CategoryId { get; }

    /// <summary>是否主分类（决定面包屑与 canonical）。部分唯一索引保证"至多一行"，见类型注释。</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>同商品多分类时的展示顺序（不参与 canonical 判定）。</summary>
    public int SortOrder { get; private set; }

    public static ProductCategory Create(Guid productId, Guid categoryId, bool isPrimary, int sortOrder = 0)
        => new(productId, categoryId, isPrimary, sortOrder);

    /// <summary>
    /// 商品详情页/管理端展示用的次要分类改序。改主分类<b>不走这里</b>——换主分类是一次
    /// "两行同时改"的操作（旧 primary 置 false、新 primary 置 true），必须由
    /// <see cref="EnsureExactlyOnePrimaryPerProduct"/> 在同一集合上校验后整体持久化，
    /// 否则"先置 false 再置 true"中间态会在部分唯一索引上短暂通过、却留下零 primary 的行。
    /// </summary>
    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>
    /// 校验"每个商品恰好一行 <see cref="IsPrimary"/> = true"，不满足则抛
    /// <c>PRODUCT_CATEGORY_PRIMARY_NOT_EXACTLY_ONE</c> 并点名具体商品。
    /// </summary>
    /// <remarks>
    /// <b>调用点（全部批量路径都必须调）</b>：fixture 装载器、基准数据生成器、CSV 导入、
    /// 以及任何"直接写 <c>ProductCategory</c>"的迁移回填。没有分类归属的商品不在此列——
    /// 判据只对"有归类意图"的商品成立，所以本方法按 <b>入参集合</b>分组，而不是去查全库。
    /// </remarks>
    /// <param name="rows">待校验的绑定行（同一商品的行可以分散在多处，本方法按 ProductId 分组）。</param>
    public static void EnsureExactlyOnePrimaryPerProduct(IEnumerable<ProductCategory> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var offenders = rows
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Primary = g.Count(r => r.IsPrimary) })
            .Where(x => x.Primary != 1)
            .OrderBy(x => x.ProductId)
            .ToList();

        if (offenders.Count == 0)
        {
            return;
        }

        var detail = string.Join(", ", offenders.Select(o => $"{o.ProductId}={o.Primary}"));
        throw new InvariantViolationException(
            "PRODUCT_CATEGORY_PRIMARY_NOT_EXACTLY_ONE",
            $"{offenders.Count} product(s) do not have exactly one primary category " +
            $"(productId=primaryRowCount): {detail}. " +
            "The partial unique index only blocks \"more than one\"; \"none\" is silent and ends up as a missing breadcrumb.");
    }
}
