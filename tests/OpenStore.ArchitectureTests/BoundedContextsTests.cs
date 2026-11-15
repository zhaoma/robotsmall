using System.Reflection;
using NetArchTest.Rules;
using OpenStore.Domain.Catalog;
using Shouldly;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 门禁步骤 3：限界上下文互引矩阵（check.sh → <c>dotnet test --filter Category=BoundedContexts</c>）。
/// </summary>
/// <remarks>
/// <para>
/// M0 落地了 5 个上下文（Catalog / Customers / Inventory / Ordering / Shared），矩阵是 5×5 而非架构文档里的 8×8。
///
/// <para><b>为什么白名单要在这里再写一遍</b>：真实存在的命名空间集合由文件系统决定，
/// 而"哪些依赖允许"由人决定。两者必须显式对齐，否则新加一个上下文会**悄悄**获得
/// 与所有其他上下文互引的自由（矩阵只对已登记的上下文生成用例，未登记的等于不受检）。
/// 这条测试就是那道对齐闸门——它在 M0 联调时确实抓到过 Inventory / Customers 未登记。</para>
/// 白名单<b>显式列出每一条允许的边</b>，未列出的边一律视为违规——这样 M1 新增
/// Inventory/Cart/Payments 时，默认结果是"新上下文谁都不许引用"（故障方向安全）。
/// </para>
/// <para>
/// <b>为什么主检测是源码而不是 NetArchTest</b>（本仓库实测结论）：NetArchTest 的
/// <c>HaveDependencyOn</c> 只看元数据签名（基类、接口、字段、属性、方法签名、特性），
/// <b>不看方法体</b>。而上下文互引最典型的形态恰恰是方法体里的
/// <c>throw new XxxException(...)</c>。实测：Catalog 中 <c>using OpenStore.Domain.Shared;</c>
/// 只服务于方法体内的抛出点，NetArchTest 报告"无依赖"，这条门禁于是空转。
/// 改用源码扫描（剥注释后查命名空间字面量）后，<c>using</c> / 全限定名 / using 别名 /
/// global using 全部覆盖，且能直接报出文件路径。NetArchTest 仍作为第二道（元数据级）
/// 检测保留，两者取并集。
/// </para>
/// </remarks>
public sealed class BoundedContextsTests
{
    private const string Catalog = "OpenStore.Domain.Catalog";
    private const string Customers = "OpenStore.Domain.Customers";
    private const string Inventory = "OpenStore.Domain.Inventory";
    private const string Ordering = "OpenStore.Domain.Ordering";
    private const string Shared = "OpenStore.Domain.Shared";

    /// <summary>白名单：只列"允许存在依赖"的边。未列出的边 = 违规。</summary>
    private static readonly HashSet<(string From, string To)> AllowedEdges = new()
    {
        (Catalog, Shared),
        (Customers, Shared),
        (Inventory, Shared),
        (Ordering, Shared),
        // Inventory -> Catalog：变体持有库存，库存要按 VariantId 定位（由仓储在 Infrastructure 层解析，
        // 领域内不得直接引用 Catalog 的聚合，所以这条边**不存在**。留注释是为了让下一个人
        // 知道"我查过了，不是漏写"——矩阵里最危险的不是错边，是没人解释的空位。）
    };

    private static string DomainDir => Path.Combine(RepoLayout.SrcDir, "OpenStore.Domain");

    private static Assembly DomainAssembly => typeof(Product).Assembly;

    public static TheoryData<string, string, bool> Matrix()
    {
        string[] contexts = { Catalog, Customers, Inventory, Ordering, Shared };
        var data = new TheoryData<string, string, bool>();

        foreach (var from in contexts)
        {
            foreach (var to in contexts)
            {
                if (from == to)
                {
                    continue; // 自引用不构成"上下文互引"
                }

                data.Add(from, to, AllowedEdges.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [Trait("Category", "BoundedContexts")]
    [MemberData(nameof(Matrix))]
    public void 上下文依赖矩阵(string fromNamespace, string toNamespace, bool allowed)
    {
        var fromDirectory = ContextDirectory(fromNamespace);

        var sourceFiles = RepoLayout.CSharpFiles(fromDirectory);
        sourceFiles.ShouldNotBeEmpty(
            $"source folder for '{fromNamespace}' is empty; the gate would be a no-op");

        // ① 源码级：using / 全限定名 / 别名 / global using 都逃不掉
        var violations = sourceFiles
            .Where(file => RepoLayout.ReadStripped(file).Contains(toNamespace, StringComparison.Ordinal))
            .Select(RepoLayout.Relative)
            .ToList();

        // ② 元数据级：补上"只在签名里出现"的情况（NetArchTest 能给出类型名）
        var metadataResult = Types.InAssembly(DomainAssembly)
            .That()
            .ResideInNamespace(fromNamespace)
            .ShouldNot()
            .HaveDependencyOn(toNamespace)
            .GetResult();

        if (!metadataResult.IsSuccessful && metadataResult.FailingTypeNames is not null)
        {
            violations.AddRange(metadataResult.FailingTypeNames.Select(name => $"{name} (metadata)"));
        }

        if (allowed)
        {
            // 白名单内的边允许存在依赖；"这条边是否还真实存在"由下一条用例保证白名单不腐化
            return;
        }

        violations.ShouldBeEmpty(
            $"{Short(fromNamespace)} must not reference {Short(toNamespace)} (found in {string.Join(", ", violations)})");
    }

    [Fact]
    [Trait("Category", "BoundedContexts")]
    public void 白名单里的每一条边都必须真实存在_防止矩阵整体写错()
    {
        foreach (var (from, to) in AllowedEdges)
        {
            var hits = RepoLayout.CSharpFilesContaining(ContextDirectory(from), to);

            hits.ShouldNotBeEmpty(
                $"whitelisted edge {Short(from)} -> {Short(to)} no longer exists; update BoundedContextsTests.AllowedEdges");
        }
    }

    [Fact]
    [Trait("Category", "BoundedContexts")]
    public void Domain下的上下文命名空间集合必须与白名单一致()
    {
        // M1 新增上下文时必须显式改这里：默认结果是"新上下文默认谁都不许引用"，
        // 而不是"自动进入矩阵"（自动发现会让矩阵的失败方向变得不安全）。
        var known = new[] { "Catalog", "Customers", "Inventory", "Ordering", "Shared" };

        var actual = Directory
            .EnumerateDirectories(DomainDir)
            .Select(dir => Path.GetFileName(dir))
            .Where(name => RepoLayout.CSharpFiles(Path.Combine(DomainDir, name)).Count > 0)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(known, ignoreOrder: true,
            customMessage: "Domain 下出现了新的限界上下文命名空间：请同步 BoundedContextsTests 的矩阵白名单");
    }

    private static string ContextDirectory(string ns)
        => Path.Combine(DomainDir, ns.Split('.')[^1]);

    private static string Short(string ns) => ns.Replace("OpenStore.Domain.", "Domain.", StringComparison.Ordinal);
}
