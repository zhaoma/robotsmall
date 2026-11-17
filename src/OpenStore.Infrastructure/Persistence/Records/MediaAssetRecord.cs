namespace OpenStore.Infrastructure.Persistence.Records;

/// <summary>
/// <c>MediaAsset</c> 表：上传的二进制资产元数据。<b>不可变表：只有 <c>CreatedAt</c>，没有 <c>UpdatedAt</c></b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不可变</b>：文件内容一旦写入就不会被"编辑"——换图是<b>新建一个资产</b>并改引用，
/// 不是原地覆盖。原地覆盖会让 CDN/浏览器缓存、以及所有引用它的商品同时看到新图，
/// 而"我只想改这一个商品的主图"就变成了全局替换。不可变让 <see cref="StorageKey"/> 与
/// <see cref="ContentHash"/> 可以永久作为缓存键。
/// </para>
/// <para>
/// <b><see cref="StorageKey"/> 唯一</b>：它是对象存储里的路径，重复写入同一个 key 会静默覆盖别人
/// 的图。唯一索引把这个错误变成插入失败。
/// </para>
/// <para>
/// <b><see cref="ContentHash"/> 普通索引（不唯一）</b>：用于秒传/去重（"同一张图已存在就别再传"），
/// 但不同文件可以有相同 hash 的前提不成立，所以它只是查找加速，不是身份标识。
/// </para>
/// <para>
/// <b><see cref="MediaType"/> 决定清理策略</b>：换主题时清理 <c>ThemeBrand</c>/<c>ThemeBlock</c>
/// 的孤儿文件；商品图必须保留。没有这个区分，"清理未被引用的媒体"会误删商品图。
/// </para>
/// </remarks>
public sealed class MediaAssetRecord
{
    public Guid Id { get; set; }

    /// <summary>对象存储路径，唯一。</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>内容 SHA-256（去重/秒传用）。</summary>
    public string ContentHash { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long ByteSize { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>存 <see cref="Domain.Catalog.MediaType"/> 的数值。</summary>
    public int MediaType { get; set; }

    /// <summary>原始文件名（仅展示/下载时用，<b>不参与存储路径</b> —— 否则中文/特殊字符会成为路径的一部分）。</summary>
    public string? OriginalName { get; set; }

    /// <summary>不可变表只有这一个时间列。</summary>
    public DateTime CreatedAt { get; set; }
}
