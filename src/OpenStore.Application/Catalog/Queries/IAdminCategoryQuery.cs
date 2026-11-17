using OpenStore.Application.Catalog.Dtos.Admin;

namespace OpenStore.Application.Catalog.Queries;

/// <summary>
/// 后台分类树读端口（只读，读模型）。读走投影、不物化聚合，与
/// <see cref="IAdminProductQuery"/> 同一约定（后端设计 §1.1「列表走读模型」）。
/// </summary>
public interface IAdminCategoryQuery
{
    /// <summary>
    /// 取完整分类树（已按 <c>ParentId</c> / <c>SortOrder</c> 排好并挂好父子关系）。
    /// </summary>
    /// <param name="locale">
    /// 译文语言（<c>zh-Hans</c> / <c>en</c>）。<b>没有该语言译文时回退到第一条可用译文</b>，
    /// 而不是返回空名字 —— 后台列表显示空白行比显示别的语言更难排查。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<AdminCategoryNodeDto>> GetTreeAsync(string locale, CancellationToken ct);
}
