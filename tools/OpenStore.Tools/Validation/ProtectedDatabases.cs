using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenStore.Infrastructure.Persistence;

namespace OpenStore.Tools.Validation;

/// <summary>
/// 收集"受保护的数据库路径"：<c>src/OpenStore.Api/appsettings*.json</c> 里每条连接串指向的库，
/// 加上设计期工厂的固定开发库。供 <c>seed-bench</c> 拒绝写入。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要扫 appsettings 而不是只比对文件名</b>：文件名是约定，配置才是事实。
/// 有人把开发库改名成 <c>local.db</c> 并写进 appsettings，文件名那道闸门就静默失效了；
/// 而"这个路径正是 Api 在用的库"这件事，只有配置能回答。
/// </para>
/// <para>
/// <b>相对路径按多个基准各解析一次</b>：<c>Data Source=./openstore.dev.db</c> 这种写法，
/// 在 <c>dotnet run</c>（cwd=仓库根）、Api 进程（contentRoot=项目目录）、容器（/data）下
/// 会指向不同文件。把每种可能都纳入受保护集合：目标是"不碰任何**可能**是开发库的路径"，
/// 而不是"精确判断哪一个才是"——误拒的代价是换个路径，误放的代价是数据丢失。
/// </para>
/// </remarks>
internal static class ProtectedDatabases
{
    public static IReadOnlyCollection<string> Collect(string? repoRoot)
    {
        var protectedPaths = new HashSet<string>(StringComparer.Ordinal);

        // 解析基准：当前工作目录、仓库根、Api 项目目录。
        var bases = new List<string> { Directory.GetCurrentDirectory() };
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            bases.Add(repoRoot);

            var apiDir = Path.Combine(repoRoot, "src", "OpenStore.Api");
            if (Directory.Exists(apiDir))
            {
                bases.Add(apiDir);

                // appsettings*.json 的 ConnectionStrings（Default 与任何命名的连接串）
                foreach (var file in Directory.EnumerateFiles(apiDir, "appsettings*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    foreach (var connectionString in ReadConnectionStrings(file))
                    {
                        Add(protectedPaths, connectionString, bases);
                    }
                }
            }
        }

        // 设计期工厂的连接串（check.sh 步骤 8 的迁移目标就是它，不一定出现在 appsettings 里）
        Add(protectedPaths, StoreDbContextFactory.DevelopmentConnectionString, bases);

        return protectedPaths;
    }

    private static IEnumerable<string> ReadConnectionStrings(string file)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            // 读不动时**不静默跳过**：跳过等于这条保护不存在，而使用者以为它在。
            // 让造数命令在入口就吵出来，比事后发现"原来它一直没生效"便宜得多。
            Console.Error.WriteLine($"seed-bench: 无法解析 {file}（{ex.GetType().Name}），受保护库清单不完整");
            yield break;
        }

        if (root?["ConnectionStrings"] is not JsonObject connections)
        {
            yield break;
        }

        foreach (var (_, node) in connections)
        {
            var connectionString = node?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                yield return connectionString;
            }
        }
    }

    private static void Add(HashSet<string> sink, string connectionString, IReadOnlyList<string> bases)
    {
        string dataSource;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            return; // 非 SQLite 连接串（如 PG）：没有本地文件可保护
        }

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return; // 内存库
        }

        foreach (var baseDirectory in bases.Distinct(StringComparer.Ordinal))
        {
            try
            {
                sink.Add(Path.GetFullPath(Path.Combine(baseDirectory, dataSource)));
            }
            catch (ArgumentException)
            {
                // 非法路径片段：换下一个解析基准
            }
        }
    }
}
