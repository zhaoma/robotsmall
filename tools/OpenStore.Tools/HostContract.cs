namespace OpenStore.Tools;

/// <summary>
/// 宿主契约常量。M0 阶段没有前端程序集可反射（主题是构建期 TS 包），
/// 所以宿主的 LayoutKey 全集与支持的清单格式版本只能写死在这里。
/// 这两处一旦与前端 theme-runtime 的 LayoutKey 联合类型漂移，
/// 门禁就会误报/漏报 —— 因此约定：改这两个常量必须同时改
/// apps/storefront 的 LayoutKey 定义，并在 PR 里成对出现。
/// </summary>
internal static class HostContract
{
    /// <summary>
    /// 宿主支持的布局键全集。只增不删（删除需 2 个 minor 版本的弃用期）。
    /// 值取自 docs/architecture/00-architecture.md §5.1.2 的 manifest 示例
    /// 与 docs/04-前端设计.md 的路由集合的并集。
    /// </summary>
    public static readonly IReadOnlyList<string> LayoutKeys = new[]
    {
        "default",
        "product",
        "collection",
        "category",
        "cart",
        "checkout",
        "page",
        "search",
        "account",
        "static",
    };

    /// <summary>宿主认识的清单/契约格式版本。不认识 → 拒绝安装/激活，不静默降级。</summary>
    public static readonly IReadOnlyList<string> SupportedFormatVersions = new[] { "1" };

    /// <summary>token 键形态：扁平点分小写（color.primary → CSS 变量 --color-primary）。</summary>
    public const string TokenKeyPattern = "^[a-z][a-z0-9]*(\\.[a-z0-9]+)+$";
}
