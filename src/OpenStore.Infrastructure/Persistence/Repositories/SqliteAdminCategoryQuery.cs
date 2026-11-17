using Microsoft.EntityFrameworkCore;
using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Catalog.Queries;

namespace OpenStore.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IAdminCategoryQuery"/> 的 SQLite 实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>一次读全表再在内存里组树，而不是递归查库</b>（<c>CategoryConfiguration</c> 的注释已定此策略）：
/// 分类总量是百级，<c>ORDER BY ParentId, SortOrder</c> 一次全取（走 <c>IX_Category_Parent_Sort</c>）
/// 后组树的代价远小于"每个父节点查一次子节点"的 N+1。
/// 分类规模若涨到万级，这里要改成按层懒加载 —— <b>届时先改索引策略再改实现</b>，
/// 否则只是把 N+1 换成了大表全扫。
/// </para>
/// <para>
/// <b>只取未软删的分类</b>：软删分类的 slug 继续占位（唯一索引不过滤软删），
/// 但它不应该出现在后台树上（否则管理员会以为还能用）。
/// 已软删分类下的子节点会变成孤儿；这里把孤儿<b>提到顶层</b>而不是丢弃 —— 丢弃会让
/// "数据确实存在但界面上找不到"变成一次无从下手的排障。
/// </para>
/// <para>
/// <b>递归深度受 <c>CK_Category_Depth ≤ 5</c> 限制</b>，所以用递归组装是安全的，
/// 不需要额外的显式栈。
/// </para>
/// </remarks>
public sealed class SqliteAdminCategoryQuery(StoreDbContext db) : IAdminCategoryQuery
{
    public async Task<IReadOnlyList<AdminCategoryNodeDto>> GetTreeAsync(string locale, CancellationToken ct)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.ParentId)
            .ThenBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.ParentId, c.SortOrder })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (categories.Count == 0)
        {
            return Array.Empty<AdminCategoryNodeDto>();
        }

        var ids = categories.Select(c => c.Id).ToList();

        // 译文一次取回并按分类分组。把"是否命中目标语言"作为第一排序键，
        // 这样 GroupBy 后取第一条就等于「目标语言优先，否则任意一条可用译文」——
        // 不必为每个分类再查一次库，也不会出现名字为空白的行。
        var translations = await db.CategoryTranslations
            .AsNoTracking()
            .Where(t => ids.Contains(t.CategoryId))
            .OrderByDescending(t => t.Locale == locale)
            .ThenBy(t => t.Locale)
            .Select(t => new { t.CategoryId, t.Slug, t.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var display = translations
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => (g.First().Slug, g.First().Name));

        // 子节点按父分组；排序已在 SQL 里完成，ToLookup 保序
        var childrenByParent = categories
            .Where(c => c.ParentId is not null)
            .ToLookup(c => c.ParentId!.Value);

        var known = categories.Select(c => c.Id).ToHashSet();

        AdminCategoryNodeDto Build(Guid id)
        {
            var category = categories.First(c => c.Id == id);
            var (slug, name) = display.TryGetValue(id, out var d) ? d : (string.Empty, string.Empty);

            var children = childrenByParent[id]
                .Select(child => Build(child.Id))
                .ToList();

            return new AdminCategoryNodeDto(id, slug, name, category.ParentId, category.SortOrder, children);
        }

        // 顶层 = 真根（ParentId 为空） + 孤儿（父级已被软删、不在本次结果里）
        var roots = categories
            .Where(c => c.ParentId is null || !known.Contains(c.ParentId.Value))
            .Select(c => Build(c.Id))
            .ToList();

        return roots;
    }
}
