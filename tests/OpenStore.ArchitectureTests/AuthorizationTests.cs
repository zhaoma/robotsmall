using System.Reflection;
using System.Text.RegularExpressions;
using OpenStore.Application.Common;
using Shouldly;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 门禁步骤 4：授权完备性（check.sh → <c>dotnet test --filter Category=Authorization</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 扫描 <c>src/OpenStore.Api/Endpoints/Admin/**/*.cs</c> 的源码文本，逐个端点块断言必须挂着
/// <b>具体权限点</b>（<c>RequireAuthorization(Permissions.Xxx)</c>）。
/// 裸 <c>[Authorize]</c> / <c>.RequireAuthorization()</c> 一律判红：它们的语义是"登录即可"，
/// 在后台等于任何低权限账号都能改商品、发货、退款——这是 AC-15 最难在评审里看出来的漏洞。
/// </para>
/// <para>
/// 同时反向检查 <see cref="Permissions.All"/> 里的每个码都被至少一个端点使用（防死码）：
/// 声明了权限点却没人挂策略，等于这个操作实际上谁都做不了（或者更糟：端点压根没鉴权）。
/// </para>
/// </remarks>
public sealed partial class AuthorizationTests
{
    private static string AdminEndpointsDir => Path.Combine(RepoLayout.SrcDir, "OpenStore.Api", "Endpoints", "Admin");

    private static string ApiDir => Path.Combine(RepoLayout.SrcDir, "OpenStore.Api");

    [Fact]
    [Trait("Category", "Authorization")]
    public void 每个admin端点都必须挂具体权限点()
    {
        var files = RepoLayout.CSharpFiles(AdminEndpointsDir);
        files.ShouldNotBeEmpty("no admin endpoint file found; the authorization gate would be a no-op");

        var violations = new List<string>();
        var endpointCount = 0;

        foreach (var file in files)
        {
            var text = RepoLayout.ReadStripped(file);
            var groupPrefix = GroupPattern().Match(text) is { Success: true } groupMatch
                ? groupMatch.Groups[1].Value
                : string.Empty;

            var matches = EndpointPattern().Matches(text);
            for (var i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                var block = text[start..end];

                endpointCount++;
                if (block.Contains(".RequireAuthorization(Permissions.", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add($"admin endpoint {Describe(matches[i], groupPrefix)} has no permission policy");
            }
        }

        endpointCount.ShouldBeGreaterThan(0, "no admin endpoint mapping found; the gate would be a no-op");
        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "Authorization")]
    public void 任何地方都不许出现裸RequireAuthorization或裸Authorize()
    {
        var violations = new List<string>();

        foreach (var file in RepoLayout.CSharpFiles(ApiDir))
        {
            var text = RepoLayout.ReadStripped(file);
            var relative = RepoLayout.Relative(file);

            if (BareRequireAuthorizationPattern().IsMatch(text))
            {
                violations.Add($"{relative}: bare .RequireAuthorization() has no permission policy");
            }

            if (BareAuthorizeAttributePattern().IsMatch(text))
            {
                violations.Add($"{relative}: bare [Authorize] has no permission policy");
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

    private static IReadOnlyList<string> PermissionConstantNames()
        => typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToList();

    private static string Describe(Match match, string groupPrefix)
    {
        var method = match.Groups[1].Value.ToUpperInvariant();
        var route = match.Groups[2].Value;

        if (string.IsNullOrEmpty(groupPrefix))
        {
            return $"{method} {route}";
        }

        return $"{method} {groupPrefix.TrimEnd('/')}/{route.TrimStart('/')}";
    }

    // [\b]Map(Get|Post|Put|Patch|Delete)(  + 第一个字符串字面量参数
    [GeneratedRegex(@"\bMap(Get|Post|Put|Patch|Delete)\s*\(\s*""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex(@"\bMapGroup\s*\(\s*""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex GroupPattern();

    [GeneratedRegex(@"\.RequireAuthorization\s*\(\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex BareRequireAuthorizationPattern();

    [GeneratedRegex(@"\[\s*Authorize\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex BareAuthorizeAttributePattern();
}
