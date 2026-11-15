namespace OpenStore.Application.Catalog.Dtos.Admin;

/// <summary>
/// 后台分类树节点（后端设计 §5.4 的分类 CRUD 契约形状）。
/// </summary>
/// <remarks>
/// <b>M0 只定义契约，端点返回空列表</b>：Category / CategoryTranslation / ProductCategory
/// 在数据模型 §2 已建模，但代码尚未落地分类聚合与持久化。M1 补分类聚合后，本 DTO 形状保持不变。
/// </remarks>
public sealed record AdminCategoryNodeDto(
    Guid Id,
    string Slug,
    string Name,
    Guid? ParentId,
    int SortOrder,
    IReadOnlyList<AdminCategoryNodeDto> Children);
