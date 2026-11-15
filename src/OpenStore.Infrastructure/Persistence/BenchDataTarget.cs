namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// <c>seed-bench</c> 的目标库前置校验：**允许往哪个库里灌 5 万行数据**。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么这条校验是这个命令最重要的部分</b>：造数命令是全仓库唯一一个"一次调用就能
/// 抹掉别人工作状态"的工具。压测数据灌进开发库之后，本地的管理员账号、手工建的十几条商品
/// 会全部消失，而受害者几乎不可能想到是这个命令干的——他们会先怀疑迁移、再怀疑 git、
/// 最后重装。所以宁可在入口处拒绝一次，也不要事后解释。
/// </para>
/// <para>
/// 两道闸门，缺一不可：
/// <list type="number">
/// <item><b>文件名</b>：<c>openstore.dev.db</c> 是 Api 的默认开发库名（appsettings 与
/// <see cref="StoreDbContextFactory.DevelopmentConnectionString"/> 都指向它），
/// 无论放在哪个目录都拒绝。</item>
/// <item><b>绝对路径集合</b>：调用方把 <c>appsettings*.json</c> 里每条连接串解析成绝对路径后传进来，
/// 命中即拒绝。这一道防的是"开发库改了名/换了目录，但配置里仍然是它"。</item>
/// </list>
/// </para>
/// <para>
/// 判定只做"是否命中"，不做"是否像开发库"的猜测：猜错的代价不对称——
/// 误拒只是让操作者换个路径，误放则是数据丢失。
/// </para>
/// </remarks>
public static class BenchDataTarget
{
    /// <summary>Api 默认开发库文件名（见 <c>src/OpenStore.Api/appsettings.json</c> 与设计期工厂）。</summary>
    public const string ProtectedFileName = "openstore.dev.db";

    /// <summary>
    /// 校验目标库是否允许写入。返回 <c>null</c> 表示放行；否则返回**给人看的拒绝原因**
    /// （调用方必须原样打印：拒绝而不说明原因，等于让人去猜规则）。
    /// </summary>
    /// <param name="dbPath">已解析成绝对路径的目标库。</param>
    /// <param name="protectedPaths">受保护的绝对路径集合（已去重、已 <see cref="Path.GetFullPath(string)"/>）。</param>
    public static string? Validate(string dbPath, IReadOnlyCollection<string> protectedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentNullException.ThrowIfNull(protectedPaths);

        var full = Path.GetFullPath(dbPath);
        var fileName = Path.GetFileName(full);

        if (string.Equals(fileName, ProtectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            return $"拒绝：目标库文件名是 '{ProtectedFileName}'（Api 的默认开发库）。" +
                "压测数据灌进去会抹掉本地管理员账号与手工数据。请换一个路径，例如 --db /tmp/bench-small.db";
        }

        foreach (var candidate in protectedPaths)
        {
            if (string.Equals(full, candidate, StringComparison.Ordinal))
            {
                return $"拒绝：目标库 '{full}' 就是 appsettings 里配置的数据库。请换一个路径，例如 --db /tmp/bench-small.db";
            }
        }

        return null;
    }
}
