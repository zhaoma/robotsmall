using System.Reflection;
using NetArchTest.Rules;
using OpenStore.Domain.Catalog;
using Shouldly;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 门禁步骤 2：分层依赖（check.sh → <c>dotnet test --filter Category=Layering</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 每条断言都在<b>编译成功之后</b>判定，而不是靠"编译不过"来体现——
/// 这是本门禁设计上最容易被做假的地方：如果 Domain 里完全没有 EF 的 PackageReference，
/// 往 Money.cs 里加 <c>using Microsoft.EntityFrameworkCore;</c> 会直接编译失败，
/// 于是 CI 确实红了，但红在编译器上，架构测试根本没跑起来，规则本身从未被验证。
/// 因此 <c>scripts/verify-gates.sh</c> 的 V1 注入必须同时改 csproj（加 PackageReference）
/// 与源码（加 using + 真实引用一个 EF 类型），被注入的代码仍可编译，此时才轮到本文件说话。
/// </para>
/// <para>
/// 本文件同时做<b>两种</b>检测：
/// <list type="number">
/// <item>元数据级：程序集引用白名单 + NetArchTest 类型依赖（精确到类型名）。</item>
/// <item>源码级：字面量扫描（连 <c>using</c> 这种"暂时没用到"的引用也逃不掉，且能直接报文件路径）。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class LayeringTests
{
    private static readonly string[] DomainForbiddenPrefixes =
    {
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Newtonsoft.Json",
    };

    private static Assembly DomainAssembly => typeof(Product).Assembly;

    private static Assembly ApplicationAssembly => typeof(Application.Common.Permissions).Assembly;

    private static Assembly InfrastructureAssembly => typeof(Infrastructure.Persistence.StoreDbContext).Assembly;

    private static Assembly ApiAssembly => typeof(Program).Assembly;

    // -------------------------------------------------------------------------
    // Domain：零第三方依赖
    // -------------------------------------------------------------------------

    [Theory]
    [Trait("Category", "Layering")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Newtonsoft.Json")]
    public void Domain_不得引用框架与第三方库(string forbiddenNamespace)
    {
        // ① 源码级：using / csproj PackageReference 都算违规，报文件路径最好定位
        var domainDir = Path.Combine(RepoLayout.SrcDir, "OpenStore.Domain");
        var hits = RepoLayout.FilesContaining(domainDir, forbiddenNamespace, includeProjectFiles: true);
        hits.ShouldBeEmpty(
            $"Domain must not reference {forbiddenNamespace} (found in {string.Join(", ", hits)})");

        // ② 元数据级：NetArchTest 按类型依赖（能精确定位到类型名）
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Domain must not reference {forbiddenNamespace} (found in {Describe(result)})");
    }

    [Fact]
    [Trait("Category", "Layering")]
    public void Domain_只能引用_System_前缀程序集与自身()
    {
        var violations = DomainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !IsAllowedForDomain(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        violations.ShouldBeEmpty(
            $"Domain must not reference {string.Join(", ", violations)} (found in OpenStore.Domain)");
    }

    // -------------------------------------------------------------------------
    // Application：不得依赖 Infrastructure，也不得依赖 EF
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Layering")]
    public void Application_不得依赖Infrastructure()
    {
        ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain("OpenStore.Infrastructure",
                "Application must not reference OpenStore.Infrastructure (found in OpenStore.Application)");

        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("OpenStore.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application must not reference OpenStore.Infrastructure (found in {Describe(result)})");
    }

    [Fact]
    [Trait("Category", "Layering")]
    public void Application_不得依赖EFCore()
    {
        ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain("Microsoft.EntityFrameworkCore",
                "Application must not reference Microsoft.EntityFrameworkCore (found in OpenStore.Application)");
    }

    // -------------------------------------------------------------------------
    // Infrastructure：不得依赖 Api
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Layering")]
    public void Infrastructure_不得依赖Api()
    {
        InfrastructureAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain("OpenStore.Api",
                "Infrastructure must not reference OpenStore.Api (found in OpenStore.Infrastructure)");
    }

    // -------------------------------------------------------------------------
    // Api：只做组合根，不得出现事务/持久化调用
    // -------------------------------------------------------------------------

    [Theory]
    [Trait("Category", "Layering")]
    [InlineData(".SaveChangesAsync")]
    [InlineData(".BeginTransaction")]
    public void Api_端点不得直接操作DbContext(string forbiddenCall)
    {
        var apiDir = Path.Combine(RepoLayout.SrcDir, "OpenStore.Api");
        var hits = RepoLayout.FilesContaining(apiDir, forbiddenCall);

        hits.ShouldBeEmpty(
            $"Api must not call {forbiddenCall} (found in {string.Join(", ", hits)}). " +
            "写操作必须经过 Application 用例 + Infrastructure 仓储。");
    }

    [Fact]
    [Trait("Category", "Layering")]
    public void Api_可以被加载_说明组合根存在()
    {
        // 防空转：如果 Api 程序集取不到，上面那条文本扫描会因为目录不存在而抛异常，
        // 这条断言把"程序集确实被引用到了"这件事也钉住。
        ApiAssembly.GetName().Name.ShouldBe("OpenStore.Api");
    }

    private static bool IsAllowedForDomain(string assemblyName)
        => assemblyName == "OpenStore.Domain"
        || assemblyName == "mscorlib"
        || assemblyName == "netstandard"
        || assemblyName == "System"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);

    private static string Describe(TestResult result)
        => result.FailingTypeNames is null || !result.FailingTypeNames.Any()
            ? "OpenStore.Domain (assembly-level reference)"
            : string.Join(", ", result.FailingTypeNames);
}
