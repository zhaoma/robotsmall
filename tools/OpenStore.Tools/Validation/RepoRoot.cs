namespace OpenStore.Tools.Validation;

/// <summary>
/// 定位仓库根（含 schema/template.schema.json 的目录）。
/// 优先用 --root；否则从当前工作目录向上找，再退到程序所在目录向上找 ——
/// 这样 `dotnet run --project tools/OpenStore.Tools`（cwd=仓库根）、
/// 直接跑 bin 下的产物、以及 CI 里在子目录中调用都能工作。
/// </summary>
internal static class RepoRoot
{
    private static readonly string[] Markers =
    {
        Path.Combine("schema", "template.schema.json"),
        Path.Combine("assets", "industry-templates"),
    };

    public static string? Resolve(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var full = Path.GetFullPath(explicitRoot);
            return IsRoot(full) ? full : null;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (IsRoot(dir.FullName))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    private static bool IsRoot(string path) => Markers.All(marker => File.Exists(Path.Combine(path, marker)) || Directory.Exists(Path.Combine(path, marker)));

    /// <summary>统一成正斜杠的相对路径，门禁输出在 Windows/macOS 上一致，脚本可以照抄断言。</summary>
    public static string Relative(string baseDirectory, string path)
        => Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
}
