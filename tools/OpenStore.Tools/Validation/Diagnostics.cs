namespace OpenStore.Tools.Validation;

/// <summary>
/// 固定错误码。docs/03-后端设计.md §9.2 的 verify-gates.sh 与 QA 用例按这些字符串断言，
/// 所以它们是**契约**，不允许随手改名或新增同义码。
/// </summary>
internal static class ErrorCodes
{
    public const string SchemaInvalid = "SCHEMA_INVALID";
    public const string KeyCoverageLow = "KEY_COVERAGE_LOW";
    public const string AxisScopeInvalid = "AXIS_SCOPE_INVALID";
    public const string RefNotFound = "REF_NOT_FOUND";
    public const string ThemeNotFound = "THEME_NOT_FOUND";
    public const string LayoutNotSupported = "LAYOUT_NOT_SUPPORTED";
    public const string TokenKeyMismatch = "TOKEN_KEY_MISMATCH";
    public const string FormatVersionUnsupported = "FORMAT_VERSION_UNSUPPORTED";

    /// <summary>区块在 blockKey.enum 里声明了，但没有对应的 props schema。
    /// 症状是"后台能插入区块、却无法配置任何东西"，商家会以为主题坏了。</summary>
    public const string BlockPropsMissing = "BLOCK_PROPS_MISSING";

    /// <summary>token 声明了但没有 default。商家看到的默认值与宿主预期不符，且无人会主动发现。</summary>
    public const string TokenDefaultMissing = "TOKEN_DEFAULT_MISSING";
}

/// <summary>一条门禁诊断。File 相对于该资产集合的根（如 womenswear/template.json），Pointer 是 JSON Pointer。</summary>
internal sealed record Diagnostic(string Code, string File, string Pointer, string Message);

/// <summary>
/// 收集 + 打印。输出格式是脚本契约：
///   {相对路径} {JSON Pointer}: {错误码} {人话说明}
///   ...
///   FAILED: n error(s)
///   GATE FAILED: {规则名}
/// 全部通过时只打印一行 `OK {集合名}: {N}`，退出码 0。
/// </summary>
internal sealed class CheckRun
{
    private readonly List<Diagnostic> _items = new();

    public int Count => _items.Count;

    public void Add(string code, string file, string pointer, string message)
        => _items.Add(new Diagnostic(code, file, pointer, message));

    public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

    public int Finish(string collection, int okCount, string gate)
    {
        if (_items.Count == 0)
        {
            Console.WriteLine($"OK {collection}: {okCount}");
            return 0;
        }

        foreach (var d in _items)
        {
            Console.WriteLine($"{d.File} {Normalize(d.Pointer)}: {d.Code} {d.Message}");
        }

        Console.WriteLine($"FAILED: {_items.Count} error(s)");
        Console.WriteLine($"GATE FAILED: {gate}");
        return 1;
    }

    /// <summary>根节点的 JSON Pointer 是空串，用 # 表示，避免输出里出现两个连续空格。</summary>
    private static string Normalize(string pointer) => string.IsNullOrEmpty(pointer) ? "#" : pointer;
}
