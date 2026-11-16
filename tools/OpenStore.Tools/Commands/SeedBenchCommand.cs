using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenStore.Infrastructure.Persistence;
using OpenStore.Tools.Validation;

namespace OpenStore.Tools.Commands;

/// <summary>
/// <c>seed-bench</c>：造 AC-12 的基准数据集（docs/06 §4.1）。
/// </summary>
/// <remarks>
/// <para>
/// 用法：
/// <code>
/// dotnet run --project tools/OpenStore.Tools -- seed-bench --db &lt;sqlite-path&gt; [--tier small|standard]
/// dotnet run --project tools/OpenStore.Tools -- seed-bench --self-test
/// </code>
/// </para>
/// <para>
/// <b>退出码只有三个</b>（本仓库已经并存两套退出码，不再造第三套）：
/// <c>0</c> 成功且"造满了"核对通过；<c>1</c> 数据不一致或自证失败；<c>2</c> 用法错误或前置条件不满足
/// （缺 <c>--db</c>、tier 非法、命中受保护库、目标目录不存在）。
/// </para>
/// <para>
/// 生成规则、幂等口径与指纹算法在 <see cref="BenchDataSeeder"/>；本文件只负责
/// 参数校验、目标库安全校验与**把结论打印成可直接贴进验收记录的形态**。
/// </para>
/// </remarks>
internal static class SeedBenchCommand
{
    public const int ExitOk = 0;

    /// <summary>数据不一致 / 自证失败。<b>不是</b>环境问题，不许重试了事。</summary>
    public const int ExitInconsistent = 1;

    /// <summary>用法错误或前置条件不满足（含"拒绝写受保护库"）。</summary>
    public const int ExitUsage = 2;

    private const string DefaultTier = "small";

    public static int Run(string? dbOption, string? tierOption, bool selfTest) => RunAsync(dbOption, tierOption, selfTest)
        .GetAwaiter()
        .GetResult();

    /// <summary>
    /// 造数主体。self-test 会以不同参数递归调用它来断言退出码，因此**所有输出都走 Console**，
    /// 不自带日志框架、不写文件——否则自证里就抓不到"它到底打印了什么原因"。
    /// </summary>
    private static async Task<int> RunAsync(string? dbOption, string? tierOption, bool selfTest)
    {
        if (selfTest)
        {
            if (!string.IsNullOrWhiteSpace(dbOption))
            {
                Console.Error.WriteLine("seed-bench: --self-test 不接受 --db（自证只在临时目录里造数，不碰任何真实库）");
                return ExitUsage;
            }

            return SeedBenchSelfTest.Run();
        }

        if (string.IsNullOrWhiteSpace(dbOption))
        {
            Console.Error.WriteLine(
                "seed-bench: 缺少必填参数 --db <sqlite-path>。" +
                "这个命令会写入数万行数据，所以不提供默认路径（默认路径是数据丢失的第一步）。" +
                "例：--db /tmp/bench-small.db");
            return ExitUsage;
        }

        var tier = string.IsNullOrWhiteSpace(tierOption) ? DefaultTier : tierOption;
        var spec = BenchDataSeeder.FindSpec(tier);
        if (spec is null)
        {
            Console.Error.WriteLine($"seed-bench: --tier 只接受 small|standard（收到 '{tier}'）");
            return ExitUsage;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(dbOption);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.Error.WriteLine($"seed-bench: --db '{dbOption}' 不是合法路径（{ex.GetType().Name}）");
            return ExitUsage;
        }

        // 受保护库校验放在建目录/建库之前：拒绝必须发生在产生任何副作用之前。
        var repoRoot = RepoRoot.Resolve(null);
        var protectedPaths = ProtectedDatabases.Collect(repoRoot);
        var refusal = BenchDataTarget.Validate(fullPath, protectedPaths);
        if (refusal is not null)
        {
            Console.Error.WriteLine($"seed-bench: {refusal}");
            return ExitUsage;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            // 不替使用者 mkdir：路径打错时会静默造出一棵空目录树 + 一个空库，
            // 而人以为自己在压那个已经存在的库。
            Console.Error.WriteLine($"seed-bench: 目标目录不存在：{directory}（本命令不创建目录）");
            return ExitUsage;
        }

        Console.WriteLine($"== seed-bench：{spec.Describe()} ==");
        Console.WriteLine($"db={fullPath}");
        Console.WriteLine($"tier={spec.Name}");
        Console.WriteLine($"seed_version={BenchDataSeeder.SeedVersion}");
        Console.WriteLine($"protected_databases={protectedPaths.Count}");

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite($"Data Source={fullPath}")
            .Options;

        await using var db = new StoreDbContext(options);

        BenchSeedReport report;
        try
        {
            report = await BenchDataSeeder.SeedAsync(db, spec, message => Console.WriteLine(message), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            // 造数失败**不是**"用法错误"：库是真的写坏了/模型真的漂了，属于数据不一致。
            Console.Error.WriteLine($"seed-bench: 写入失败（{ex.GetType().Name}）：{ex.Message}");
            return ExitInconsistent;
        }

        PrintReport(report);

        return report.Verification.IsComplete ? ExitOk : ExitInconsistent;
    }

    /// <summary>
    /// 结论块。<b>格式是契约</b>：soak 报告要贴它，"造满了"的判据要按 <c>key=value</c> 解析它，
    /// 所以只增不改（新增字段追加在末尾，不改既有键名）。
    /// </summary>
    private static void PrintReport(BenchSeedReport report)
    {
        var verification = report.Verification;

        Console.WriteLine();
        Console.WriteLine("== 规格与数据量指纹（可直接贴进验收记录） ==");
        Console.WriteLine($"seed_version={BenchDataSeeder.SeedVersion}");
        Console.WriteLine($"tier={report.Spec.Name}");
        Console.WriteLine($"spec_products={report.Spec.ProductCount}");
        Console.WriteLine($"spec_variants={report.Spec.VariantCount}");
        Console.WriteLine($"spec_categories={report.Spec.CategoryCount}");
        Console.WriteLine($"spec_products_per_category={report.Spec.ProductsPerCategory}");
        Console.WriteLine($"products={verification.Products}");
        Console.WriteLine($"variants={verification.Variants}");
        Console.WriteLine($"status_active={verification.Active}");
        Console.WriteLine($"status_draft={verification.Draft}");
        Console.WriteLine($"products_with_wrong_variant_count={verification.UnevenProducts}");
        Console.WriteLine($"duplicate_axis_signatures={verification.DuplicateAxisSignatures}");

        // 这一项的**成立范围**必须跟着数据一起打印，不能只写在文档里：
        // 报告一旦被摘出来单独转发，"每分类 500" 会被直接读成"分类页指标已达标"。
        //
        // 措辞里**不许写"M0 无 ProductCategory 表"**：那张表已经随 M1 目录迁移建好了
        // （ad65e6b）。现在口径还是 slug 段的真实原因是**本造数器没写那两张表**，
        // 而不是表不存在 —— 前者是"我们的活没干完"，后者是"平台还没有这个能力"，
        // 两者的到期日不同，混在一句里会让下一个读的人等错东西。
        Console.WriteLine("category_check_scope=slug-segment（原因不是\"表不存在\"：Category/ProductCategory 已随 M1 目录迁移建表；" +
                          "是本造数器还没写这两张表 ⇒ 分类归属未进库，join 成本仍未被证明）");

        // 「每分类 500」只描述**直属**成员语义。而 AC-12 量的是分类页，分类页有两种都合法的
        // 成员语义（02 §4.9 第 ② 行两条路都在用）：直属 `WHERE CategoryId=?`、子树 `Path LIKE`。
        // 同一份数据在两种语义下"最贵的那一页"差一个数量级 —— 这两个数必须分开出现，
        // 否则"每分类 500 ✅"会被读成"最坏页也 500"，而实际最坏页是根分类的 5000。
        // 判据：**数量对了但最坏用例没覆盖，等于没测最该测的那一页。**
        var levels = report.Spec.SubtreeProductsByDepth();
        var levelText = string.Join("、", levels.Select(l => $"d{l.Depth}:{l.CategoryCount}个×{l.SubtreeProducts}件"));
        var worstSubtree = levels.Count == 0 ? 0 : levels.Max(l => l.SubtreeProducts);
        Console.WriteLine($"category_subtree_products={levelText}");

        // 语义已裁定（06 §4.1：分类页含子树商品，AC-12 基线页 = 根分类页），所以这里**不能**
        // 再写"未定"。这行是数据侧的读数指引：造数器知道最贵那页有多大，压测才知道该压哪一页。
        // 反过来，若哪天裁定变了而这行没跟着改，报告会拿叶分类页的漂亮数字去回答一个关于根分类页的问题。
        Console.WriteLine(
            $"ac12_category_page=根分类页（子树 {worstSubtree} 件；语义已裁定「分类页含子树」，见 06 §4.1——" +
            $"直属 {report.Spec.ProductsPerCategory} 那一页不是 AC-12 的基线页）");

        Console.WriteLine($"inserted_products={report.InsertedProducts}");
        Console.WriteLine($"inserted_variants={report.InsertedVariants}");
        Console.WriteLine($"skipped_products={report.SkippedProducts}");
        Console.WriteLine($"fingerprint={report.Fingerprint}");

        foreach (var table in report.Tables.OrderBy(t => t.Table, StringComparer.Ordinal))
        {
            Console.WriteLine($"table_{table.Table}_rows={table.Rows}");
            Console.WriteLine($"table_{table.Table}_sha256={table.Sha256}");
        }

        if (report.SkippedProducts == report.Spec.ProductCount && report.InsertedProducts == 0)
        {
            Console.WriteLine($"已存在，跳过 {report.SkippedProducts} 个商品（按 slug 判重；再跑一次结果相同）");
        }

        // `verdict_code` 与 `verdict` **由同一个分支产出**，所以两者不可能不一致 ——
        // 这是"只有一处判断"的落地形态，不是两份真相。
        // 为什么要多一个键：`verdict` 的正文自带 `=`（`行数 = 规格`），任何
        // `cut -d= -f2` 这类最省事的读法都会把它切成半截中文。修在消费侧只救得了
        // 一个消费者；这个键是我印出去的，第二个消费者还会踩一次 —— 所以修在源头。
        if (!verification.IsComplete)
        {
            foreach (var problem in verification.Problems)
            {
                Console.WriteLine($"CHECK FAILED: {problem}");
            }

            Console.WriteLine("verdict_code=FAILED");
            Console.WriteLine("verdict=FAILED 造不满：行数 != 规格，这份数据不能用来出压测报告");
            return;
        }

        Console.WriteLine("verdict_code=OK");
        Console.WriteLine("verdict=OK 造满了（行数 = 规格，每分类 = 规格）");
    }

    /// <summary>把一次运行的全部控制台输出抓成字符串，供自证断言"它打印了什么"。</summary>
    internal static (int Code, string Output) Capture(Func<int> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);
            return (action(), writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
