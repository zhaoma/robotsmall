using System.Reflection;
using System.Text.RegularExpressions;
using OpenStore.Application.Catalog.Dtos.Storefront;
using Shouldly;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 门禁步骤 5：storefront 数据隔离（check.sh → <c>dotnet test --filter Category=StorefrontDto</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 反射扫描 <c>OpenStore.Application</c> 中命名空间含 <c>Storefront</c> 的 DTO，
/// 属性名命中后台字段黑名单即判红。<b>为什么靠命名而不是靠人工评审</b>：
/// "给前台 DTO 顺手加个 OnHand 方便显示库存"是极常见的一次性改动，
/// 而它泄漏的是竞争情报（Reserved 能反推秒杀节奏），评审时几乎看不出来。
/// </para>
/// <para>
/// 黑名单里的 <c>Reference</c> / <c>Voucher</c> 是支付渠道的交易号与优惠券码：
/// 前者泄漏商户号关联关系，后者可被枚举撞库。
/// </para>
/// </remarks>
public sealed partial class StorefrontDtoTests
{
    private static Assembly ApplicationAssembly => typeof(StorefrontProductDto).Assembly;

    [Fact]
    [Trait("Category", "StorefrontDto")]
    public void storefront命名空间下的DTO属性名不得命中后台字段黑名单()
    {
        var storefrontTypes = ApplicationAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Namespace is not null
                        && t.Namespace.Contains("Storefront", StringComparison.Ordinal))
            .ToList();

        // 防空转：DTO 被改名/移动后，这条断言必须失败，而不是"没扫到所以通过"
        storefrontTypes.ShouldNotBeEmpty("no storefront DTO found; the data-isolation gate would be a no-op");

        var violations = new List<string>();
        foreach (var type in storefrontTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (ForbiddenFieldPattern().IsMatch(property.Name))
                {
                    violations.Add($"{type.Name}.{property.Name} leaks admin field");
                }
            }
        }

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "StorefrontDto")]
    public void storefront_DTO不得继承admin_DTO()
    {
        var violations = new List<string>();

        foreach (var type in ApplicationAssembly.GetTypes()
                     .Where(t => t.Namespace?.Contains("Storefront", StringComparison.Ordinal) == true))
        {
            var baseType = type.BaseType;
            while (baseType is not null && baseType != typeof(object))
            {
                if (baseType.Namespace?.Contains("Admin", StringComparison.Ordinal) == true)
                {
                    violations.Add($"{type.Name} inherits admin DTO {baseType.Name}");
                }

                baseType = baseType.BaseType;
            }
        }

        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    [Trait("Category", "StorefrontDto")]
    public void StorefrontProductDto的属性集必须精确等于契约_新增字段必须先过本用例()
    {
        var actual = typeof(StorefrontProductDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            new[] { "AmountMinor", "Currency", "IsAvailable", "Slug", "Title" },
            ignoreOrder: true,
            customMessage: "StorefrontProductDto 的属性集是前后端契约（后端设计 §5.3.1），变更必须同步前端 SDK");
    }

    [GeneratedRegex(
        "(Cost|Margin|InternalNote|OnHand|Reserved|Sold|ProviderTxn|Email|AdminId|Reference|Voucher)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenFieldPattern();
}
