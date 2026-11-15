namespace OpenStore.ArchitectureTests;

/// <summary>
/// 源码文本扫描类测试需要的仓库定位工具。
/// </summary>
/// <remarks>
/// 不通过 csproj 把路径注入（那样在 IDE 里跑测试会拿到未展开的相对路径），
/// 而是在运行时从测试程序集输出目录向上找 <c>OpenStore.sln</c>。找不到就立刻炸——
/// 因为"静默扫不到文件"会让所有源码扫描类门禁变成空转（这正是最危险的失败方式）。
/// </remarks>
internal static class RepoLayout
{
    /// <summary>csproj 里的 XML 注释（与 .cs 的注释同样不参与门禁判断）。</summary>
    private static readonly System.Text.RegularExpressions.Regex XmlCommentPattern =
        new("<!--.*?-->", System.Text.RegularExpressions.RegexOptions.Singleline);

    public static string Root { get; } = FindRoot();

    public static string SrcDir => Path.Combine(Root, "src");

    public static string SlnPath => Path.Combine(Root, "OpenStore.sln");

    /// <summary>枚举目录下所有 .cs 文件，跳过 bin/obj（否则会扫到 NuGet 缓存里的生成代码）。</summary>
    public static IReadOnlyList<string> CSharpFiles(string absoluteDirectory)
    {
        if (!Directory.Exists(absoluteDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {absoluteDirectory}");
        }

        return Directory
            .EnumerateFiles(absoluteDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> ProjectFiles(string absoluteDirectory)
        => Directory.Exists(absoluteDirectory)
            ? Directory.EnumerateFiles(absoluteDirectory, "*.csproj", SearchOption.AllDirectories)
                .Where(IsSourceFile)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList()
            : Array.Empty<string>();

    /// <summary>
    /// 返回所有（剥掉注释后）包含 <paramref name="needle"/> 的源文件，路径相对仓库根。
    /// </summary>
    /// <remarks>
    /// 注释必须被剥掉：文档里提到被禁止的字面量（例如"这里不允许出现 .SaveChangesAsync"）
    /// 不该算违规，否则门禁会逼着团队把规则说明从代码里删掉。详见 <see cref="SourceText"/>。
    /// </remarks>
    public static IReadOnlyList<string> FilesContaining(string absoluteDirectory, string needle, bool includeProjectFiles = false)
    {
        var files = CSharpFiles(absoluteDirectory).ToList();
        if (includeProjectFiles)
        {
            files.AddRange(ProjectFiles(absoluteDirectory));
        }

        return files
            .Where(path => ReadStripped(path).Contains(needle, StringComparison.Ordinal))
            .Select(Relative)
            .ToList();
    }

    /// <summary>读取并剥掉注释（.cs 走 C# 注释剥离器，.csproj 走 XML 注释剥离）。</summary>
    public static string ReadStripped(string absolutePath)
    {
        var text = File.ReadAllText(absolutePath);

        return absolutePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? XmlCommentPattern.Replace(text, string.Empty)
            : SourceText.StripComments(text);
    }

    public static IReadOnlyList<string> CSharpFilesContaining(string absoluteDirectory, string needle)
        => CSharpFiles(absoluteDirectory)
            .Where(path => ReadStripped(path).Contains(needle, StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

    /// <summary>把目录下所有 .cs 剥注释后拼成一份文本（用于"某符号是否被使用"这类判断）。</summary>
    public static string ReadAll(string absoluteDirectory)
        => string.Join('\n', CSharpFiles(absoluteDirectory).Select(ReadStripped));

    public static string Relative(string absolutePath)
        => Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');

    private static bool IsSourceFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        return !normalized.Contains("/bin/", StringComparison.Ordinal)
            && !normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenStore.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate OpenStore.sln by walking up from '{AppContext.BaseDirectory}'. " +
            "Source-scanning architecture gates cannot run without the repository root.");
    }
}
