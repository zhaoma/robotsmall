using System.Reflection;
using System.Text.RegularExpressions;
using OpenStore.Application.Common;
using OpenStore.Domain.Customers;
using Shouldly;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 门禁步骤 4：授权完备性（check.sh → <c>dotnet test --filter Category=Authorization</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 扫描 <c>src/OpenStore.Api/Endpoints/**/*.cs</c> 的源码文本，逐个端点块断言它<b>显式声明</b>了授权类别，
/// 且类别与所在目录相符。<b>为什么必须"显式"</b>：本应用没有 <c>FallbackPolicy</c>，
/// 一个既不写 <c>RequireAuthorization</c> 也不写 <c>AllowAnonymous</c> 的端点<b>默认匿名可访问</b>——
/// 也就是说"忘了写授权"与"故意公开"在运行时不可区分，只有在源码层才能分开。
/// </para>
/// <para>
/// <b>扫描范围曾经只到 <c>Endpoints/Admin/</c>，这是一个真实漏口</b>：<c>AdminAuthEndpoints</c> 因为
/// "不该被要求挂权限点"被挪到了 <c>Endpoints/AdminAuth/</c>，于是它同时逃出了这条门禁的视野——
/// 在该目录里新增一个完全没写授权的端点，四周门禁全绿。现在改为扫描整个 <c>Endpoints/</c>，
/// 并允许 <c>Endpoints/Admin/</c> 之外的端点通过<b>登记在豁免表里</b>来满足规则。
/// </para>
/// <para>
/// 豁免表是"全 Api 唯一真源"，且<b>双向校验</b>：表里有而代码里没有 → 判红（表腐烂后没人删），
/// 代码里有而表里没有 → 判红（偷偷加了匿名入口）。只做单向校验的表会稳定地长出多余条目，
/// 而多余条目会让下一个人以为"本来就允许"。
/// </para>
/// <para>
/// 裸 <c>[Authorize]</c> / <c>.RequireAuthorization()</c> 的语义是"登录即可"。在后台那等于任何低权限账号
/// 都能改商品、发货、退款——这是 AC-15 最难在评审里看出来的漏洞，因此默认判红，
/// 只有语义上确实"作用于自己"的端点（改自己的密码）才允许，且必须逐条登记。
/// </para>
/// </remarks>
public sealed partial class AuthorizationTests
{
    private const string EndpointsRelativePrefix = "src/OpenStore.Api/Endpoints/";

    /// <summary>
    /// 匿名端点豁免表：<c>{相对 Endpoints/ 的文件}|{方法} {路由}</c> → 为什么它可以匿名。
    /// </summary>
    /// <remarks>
    /// <b>必须逐条写理由</b>：没有理由的豁免等于"这条规则对这里不适用"，而下一个人会照抄那一行。
    /// </remarks>
    private static readonly Dictionary<string, string> AnonymousAllowList = new(StringComparer.Ordinal)
    {
        ["AdminAuth/AdminAuthEndpoints.cs|POST /login"] = "登录入口，认证之前必须能访问",
        ["AdminAuth/AdminAuthEndpoints.cs|POST /logout"] = "登出必须在会话已失效/被停用时仍可调用，挂权限点会让用户退不出去",
        ["AdminAuth/AdminAuthEndpoints.cs|GET /me"] = "会话探测：未登录要回 401 而不是 403，前端据此决定是否跳登录页",
        ["Storefront/ProductEndpoints.cs|GET /products/{slug}"] = "前台商品详情页，公开可爬",
        ["HealthEndpoints.cs|GET /health/live"] = "存活探针，编排器在认证之前访问",
        ["HealthEndpoints.cs|GET /health/ready"] = "就绪探针，同上",
        ["HealthEndpoints.cs|GET /api/health"] = "M0 兼容探针，部署脚本依赖它",
    };

    /// <summary>
    /// 裸授权豁免表：只要求"已登录"，不要求任何具体权限点。
    /// </summary>
    /// <remarks>
    /// 只登记<b>作用于调用者自己</b>的操作——主体从 <c>ICurrentUser</c> 取、请求体里没有可越权的资源标识。
    /// 一旦某个端点开始接收"别人的" id，它就必须离开本表并挂具体权限点。
    /// </remarks>
    private static readonly Dictionary<string, string> AuthenticatedOnlyAllowList = new(StringComparer.Ordinal)
    {
        ["AdminAuth/AdminAuthEndpoints.cs|POST /change-password"] = "改自己的密码；主体取自 ICurrentUser，入参无 actorId",
    };

    private static string EndpointsDir => Path.Combine(RepoLayout.SrcDir, "OpenStore.Api", "Endpoints");

    private static string AdminEndpointsDir => Path.Combine(EndpointsDir, "Admin");

    private static string ApiDir => Path.Combine(RepoLayout.SrcDir, "OpenStore.Api");

    [Fact]
    [Trait("Category", "Authorization")]
    public void 每个端点都必须显式声明授权类别()
    {
        var sites = EndpointSites();
        sites.ShouldNotBeEmpty("no endpoint mapping found; the authorization gate would be a no-op");

        var violations = new List<string>();

        foreach (var site in sites)
        {
            var declared = CountDeclarations(site.Block);
            if (declared == 0)
            {
                // 无 FallbackPolicy ⇒ 这个端点此刻是匿名的
                violations.Add($"{site.Key} declares no authorization at all (anonymous by default)");
            }
            else if (declared > 1)
            {
                // AllowAnonymous + RequireAuthorization 同时出现时，AllowAnonymous 胜出，读代码的人会误判
                violations.Add($"{site.Key} mixes AllowAnonymous with an authorization policy");
            }
        }

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void Endpoints_Admin_下每个端点必须挂具体权限点()
    {
        var sites = EndpointSites()
            .Where(s => s.File.StartsWith("Admin/", StringComparison.Ordinal))
            .ToList();

        sites.ShouldNotBeEmpty("no admin endpoint file found; the authorization gate would be a no-op");

        var violations = sites
            .Where(s => !s.Block.Contains(".RequireAuthorization(Permissions.", StringComparison.Ordinal))
            .Select(s => $"{s.Key} has no permission policy")
            .ToList();

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void 匿名与裸授权只能出现在豁免表里()
    {
        var sites = EndpointSites();
        var violations = new List<string>();

        foreach (var site in sites)
        {
            var isAnonymous = site.Block.Contains(".AllowAnonymous()", StringComparison.Ordinal);
            var isBareAuth = BareRequireAuthorizationPattern().IsMatch(site.Block);

            if (isAnonymous && !AnonymousAllowList.ContainsKey(site.Key))
            {
                violations.Add($"{site.Key} is AllowAnonymous but not registered in the anonymous allow-list");
            }

            if (isBareAuth && !AuthenticatedOnlyAllowList.ContainsKey(site.Key))
            {
                violations.Add($"{site.Key} uses bare .RequireAuthorization() outside the authenticated-only allow-list");
            }
        }

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void 豁免表不得残留已不存在的端点()
    {
        // 只做"代码里有、表里没有"的校验时，表会稳定地长出死条目；
        // 而死条目会让后来者以为同类端点"本来就允许"。
        var keys = EndpointSites().Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        var stale = AnonymousAllowList.Keys
            .Concat(AuthenticatedOnlyAllowList.Keys)
            .Where(key => !keys.Contains(key))
            .ToList();

        stale.ShouldBeEmpty($"allow-list entries no longer match any endpoint: {string.Join(", ", stale)}");
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void 任何地方都不许出现裸RequireAuthorization或裸Authorize()
    {
        var violations = new List<string>();
        var allowListedFiles = AuthenticatedOnlyAllowList.Keys
            .Select(key => key[..key.IndexOf('|')])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in RepoLayout.CSharpFiles(ApiDir))
        {
            var text = RepoLayout.ReadStripped(file);
            var relative = RepoLayout.Relative(file);

            // 端点级的裸授权由「匿名与裸授权只能出现在豁免表里」逐条校验（带路由），
            // 这里只兜住"非端点位置"（例如整个 MapGroup 上挂裸授权），那种写法不受豁免表保护。
            var fileKey = relative.StartsWith(EndpointsRelativePrefix, StringComparison.Ordinal)
                ? relative[EndpointsRelativePrefix.Length..]
                : null;

            if (BareAuthorizeAttributePattern().IsMatch(text))
            {
                violations.Add($"{relative}: bare [Authorize] has no permission policy");
            }

            if (fileKey is not null && allowListedFiles.Contains(fileKey))
            {
                continue;
            }

            if (BareRequireAuthorizationPattern().IsMatch(text))
            {
                violations.Add($"{relative}: bare .RequireAuthorization() has no permission policy");
            }
        }

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void Permissions_All_里的每个码都必须被至少一个端点使用()
    {
        var apiSource = RepoLayout.ReadAll(ApiDir);

        var unused = PermissionConstantNames()
            .Where(name => !apiSource.Contains($"Permissions.{name}", StringComparison.Ordinal))
            .ToList();

        unused.ShouldBeEmpty(
            $"permission code(s) declared but never used by any endpoint: {string.Join(", ", unused)}");
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void 权限码字面量只允许出现在Permissions声明处()
    {
        var allowedFile = Path.Combine("src", "OpenStore.Application", "Common", "Permissions.cs")
            .Replace('\\', '/');

        var offenders = RepoLayout.CSharpFiles(RepoLayout.SrcDir)
            .Where(file => !RepoLayout.Relative(file).EndsWith(allowedFile, StringComparison.Ordinal))
            .Where(file => RepoLayout.ReadStripped(file).Contains("perm:", StringComparison.Ordinal))
            .Select(RepoLayout.Relative)
            .ToList();

        offenders.ShouldBeEmpty(
            $"\"perm:\" literals must only be declared in {allowedFile} (found in {string.Join(", ", offenders)})");
    }

    /// <summary>
    /// Staff 必须走独立清单，不允许复用 <see cref="Permissions.All"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是一条会随批次静默变质的规则，所以必须用引用相等来钉</b>：<c>All</c> 会随着每个新批次
    /// 自动长大，而 §6.4 里 <c>catalog.delete</c> / <c>refund.*</c> 等是 Admin-only。
    /// 若 <c>ForRole(Staff)</c> 直接返回 <c>All</c>，那么"把 Admin-only 码加进 All"这个动作就等于
    /// <b>给全体员工提权</b>，且没有任何一行 diff 会显示"Staff 新增了权限"。
    /// </para>
    /// <para>
    /// 用 <c>ReferenceEquals</c> 而不是比较集合内容：内容比较在两边<b>恰好</b>相同时会通过，
    /// 而那正是最危险的时刻（当前 5 个码全部共享给两种角色，内容一模一样）。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Authorization")]
    public void Staff权限必须是独立清单而不是All()
    {
        ReferenceEquals(Permissions.ForRole(AdminRole.Staff), Permissions.All)
            .ShouldBeFalse("AdminRole.Staff must return the explicit StaffCodes list, never All - otherwise the next Admin-only permission is silently granted to every staff account");

        Permissions.ForRole(AdminRole.Staff).ShouldBe(Permissions.StaffCodes);
    }

    /// <summary>Staff 清单不得出现拼错或未声明的权限码（拼错的码等于员工丢了一半能力，且没人会报错）。</summary>
    [Fact]
    [Trait("Category", "Authorization")]
    public void Staff清单必须是All的子集()
    {
        var unknown = Permissions.StaffCodes
            .Where(code => !Permissions.All.Contains(code, StringComparer.Ordinal))
            .ToList();

        unknown.ShouldBeEmpty($"StaffCodes contains permission(s) not declared in All: {string.Join(", ", unknown)}");
    }

    private static IReadOnlyList<string> PermissionConstantNames()
        => typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToList();

    /// <summary>
    /// 一个端点映射及其跟随的整条方法链（<c>.WithName</c> / <c>.Produces</c> / <c>.AllowAnonymous</c> / <c>.RequireAuthorization</c>）。
    /// </summary>
    /// <param name="File">相对 <c>src/OpenStore.Api/Endpoints/</c> 的路径（豁免表的键以此开头，换机器不变）。</param>
    /// <param name="Method">HTTP 方法（大写）。</param>
    /// <param name="Route">方法内的路由字面量。</param>
    /// <param name="Block">源码块。</param>
    private sealed record EndpointSite(string File, string Method, string Route, string Block)
    {
        public string Key => $"{File}|{Method} {Route}";
    }

    private static int CountDeclarations(string block)
    {
        var count = 0;

        if (block.Contains(".RequireAuthorization(Permissions.", StringComparison.Ordinal))
        {
            count++;
        }

        if (BareRequireAuthorizationPattern().IsMatch(block))
        {
            count++;
        }

        if (block.Contains(".AllowAnonymous()", StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static List<EndpointSite> EndpointSites()
    {
        var sites = new List<EndpointSite>();

        foreach (var file in RepoLayout.CSharpFiles(EndpointsDir))
        {
            var relative = RepoLayout.Relative(file);
            var fileKey = relative.StartsWith(EndpointsRelativePrefix, StringComparison.Ordinal)
                ? relative[EndpointsRelativePrefix.Length..]
                : relative;

            var text = RepoLayout.ReadStripped(file);
            var matches = EndpointPattern().Matches(text);

            for (var i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

                sites.Add(new EndpointSite(
                    fileKey,
                    matches[i].Groups[1].Value.ToUpperInvariant(),
                    matches[i].Groups[2].Value,
                    text[start..end]));
            }
        }

        return sites;
    }

    [GeneratedRegex(@"\bMap(Get|Post|Put|Patch|Delete)\s*\(\s*""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex(@"\.RequireAuthorization\s*\(\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex BareRequireAuthorizationPattern();

    [GeneratedRegex(@"\[\s*Authorize\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex BareAuthorizeAttributePattern();
}
