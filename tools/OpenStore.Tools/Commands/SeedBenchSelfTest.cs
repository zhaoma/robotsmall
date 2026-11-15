using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenStore.Infrastructure.Persistence;
using OpenStore.Tools.Validation;

namespace OpenStore.Tools.Commands;

/// <summary>
/// <c>seed-bench --self-test</c>：证明这个命令的判据**会红**，而且红在预期的那条断言上。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么造数命令需要自证</b>：造数工具的失败方式全部是"看起来成功了"——
/// 少造一半（压测绿得漂亮，因为负载轻了）、第二次跑又插一遍（行数翻倍，指纹变了但没人看）、
/// 灌进了开发库（本地数据没了）。这三种都不会让调用方看到非零退出码，
/// 除非这里逐条把它们变成会红的断言。
/// </para>
/// <para>
/// <b>每条都断言"红在预期断言上"</b>：只判退出码非零不够——脚本崩了也是非零。
/// 因此每条同时断言 ① 退出码 ② 输出里的结论行/原因片段，与 <c>verify-gates.sh</c> 的判法同一套。
/// </para>
/// <para>
/// <b>只在系统临时目录里造数</b>：先断言临时根不在仓库内，再开始；结束时整棵删除。
/// "绝不碰仓库内任何 .db"这句话写在注释里没有意义，写成断言才有。
/// </para>
/// </remarks>
internal static class SeedBenchSelfTest
{
    private const string Tier = "small";

    /// <summary>门禁/自证类文件必须保持"外部审查者"：不得引用 src/ 下的程序集。</summary>
    private static readonly string[] ForbiddenNamespaces =
    {
        "OpenStore.Infrastructure",
        "OpenStore.Domain",
        "OpenStore.Application",
        "OpenStore.Api",
    };

    /// <summary>
    /// 受保护库的**真实文件名**，故意写字面量而不是引用
    /// <see cref="BenchDataTarget.ProtectedFileName"/>。
    /// </summary>
    /// <remarks>
    /// 复用那个常量会让本条用例变成自指：把常量改成别的值，用例跟着改输入，
    /// 依旧"通过"——实测过，确实会假绿。判据必须锚在契约上（这个文件名是数据丢失的唯一防线），
    /// 不能锚在被测代码自己身上。
    /// </remarks>
    private const string ProtectedFileNameLiteral = "openstore.dev.db";

    /// <summary>允许引用 src/ 的文件（造数命令本身必须用 StoreDbContext 与域模型）。</summary>
    private static readonly string[] ExternalReviewerExemptFiles =
    {
        Path.Combine("Commands", "SeedBenchCommand.cs"),
        Path.Combine("Commands", "SeedBenchSelfTest.cs"),
        Path.Combine("Validation", "ProtectedDatabases.cs"),
    };

    public static int Run()
    {
        Console.WriteLine("== seed-bench --self-test（fail-before-pass：每条都必须红在预期断言上）==");

        var results = new List<CaseResult>();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"openstore-seed-bench-{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}");

        var repoRoot = RepoRoot.Resolve(null);
        if (repoRoot is not null && IsUnder(tempRoot, repoRoot))
        {
            Console.Error.WriteLine($"seed-bench self-test: 临时目录落在仓库内（{tempRoot}），拒绝运行");
            return SeedBenchCommand.ExitInconsistent;
        }

        try
        {
            Directory.CreateDirectory(tempRoot);

            var benchDb = Path.Combine(tempRoot, "bench-small.db");
            var firstFingerprint = CaseIdempotentSameDb(benchDb, results);
            CaseDeterministicAcrossDbs(firstFingerprint, Path.Combine(tempRoot, "bench-small-b.db"), results);
            CaseFingerprintAndCompletenessCanGoRed(benchDb, firstFingerprint, results);
            CaseMissingDb(results);
            CaseProtectedFileName(tempRoot, results);
            CaseInvalidTier(tempRoot, results);
            CaseGateCommandsStayExternal(repoRoot, results);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        return Report(results);
    }

    // ---------------------------------------------------------------------
    // 1) 同一个库上跑两次 → 不重复插入、行数不变、第二次打印"已存在，跳过"
    //
    // 注意本条**不能**用来证明"确定性"：第二次跑什么都没插，两次数出来的指纹必然相同，
    // 哪怕是 Guid.NewGuid() 造的也会绿。这正是在本文件上做过注入才发现的假绿
    // （把 DeterministicGuid 换成 Guid.NewGuid()，本条依旧 ok）——真正的确定性判据是下一条。
    // ---------------------------------------------------------------------
    private static string CaseIdempotentSameDb(string dbPath, List<CaseResult> results)
    {
        const string name = "idempotent_same_db";

        var first = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(dbPath, Tier, selfTest: false));
        if (first.Code != SeedBenchCommand.ExitOk)
        {
            results.Add(new CaseResult(name, false, $"第一次运行应 rc=0，实际 rc={first.Code}\n{first.Output}"));
            return string.Empty;
        }

        var second = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(dbPath, Tier, selfTest: false));
        if (second.Code != SeedBenchCommand.ExitOk)
        {
            results.Add(new CaseResult(name, false, $"第二次运行应 rc=0（幂等），实际 rc={second.Code}\n{second.Output}"));
            return string.Empty;
        }

        var fingerprintFirst = Value(first.Output, "fingerprint");
        var fingerprintSecond = Value(second.Output, "fingerprint");

        var problems = new List<string>();
        if (string.IsNullOrEmpty(fingerprintFirst) || fingerprintFirst.Length != 16)
        {
            problems.Add($"第一次没有打印 16 位 fingerprint（'{fingerprintFirst}'）");
        }

        if (!string.Equals(fingerprintFirst, fingerprintSecond, StringComparison.Ordinal))
        {
            problems.Add($"两次指纹不同：{fingerprintFirst} vs {fingerprintSecond}（说明主键或内容不确定）");
        }

        foreach (var key in new[] { "products", "variants", "table_Product_rows", "table_ProductVariant_rows" })
        {
            var a = Value(first.Output, key);
            var b = Value(second.Output, key);
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                problems.Add($"{key} 两次不同：{a} vs {b}");
            }
        }

        // 幂等的可见证据：第二次必须说"已存在，跳过"，而不是静默重插一遍。
        if (!second.Output.Contains("已存在，跳过", StringComparison.Ordinal))
        {
            problems.Add("第二次运行没有打印「已存在，跳过」——幂等对使用者不可见");
        }

        if (Value(second.Output, "inserted_products") != "0")
        {
            problems.Add($"第二次运行应当一个都不插（inserted_products=0），实际 {Value(second.Output, "inserted_products")}");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0
                ? $"第二次 inserted_products=0，行数与指纹不变（{fingerprintFirst}）"
                : string.Join("; ", problems)));

        return fingerprintFirst ?? string.Empty;
    }

    // ---------------------------------------------------------------------
    // 1b) 确定性：**另一个空库**跑同一 tier → 指纹必须完全相同
    //
    // 判据只能这样测：主键若来自 Guid.NewGuid()，两个库的 Id 不同 → 指纹不同 → 红。
    // 这是"同一个 tier 跑两次，行数相同、指纹相同"唯一能落地的测法。
    // ---------------------------------------------------------------------
    private static void CaseDeterministicAcrossDbs(string fingerprintA, string dbPath, List<CaseResult> results)
    {
        const string name = "deterministic_across_dbs";

        if (string.IsNullOrEmpty(fingerprintA))
        {
            results.Add(new CaseResult(name, false, "前置用例未产出指纹，本条无法判定（不允许静默跳过）"));
            return;
        }

        var run = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(dbPath, Tier, selfTest: false));
        var problems = new List<string>();

        if (run.Code != SeedBenchCommand.ExitOk)
        {
            problems.Add($"在新库上运行应 rc=0，实际 rc={run.Code}\n{run.Output}");
        }

        var fingerprintB = Value(run.Output, "fingerprint");
        if (!string.Equals(fingerprintA, fingerprintB, StringComparison.Ordinal))
        {
            problems.Add($"同一 tier 在两个空库上的指纹不同：{fingerprintA} vs {fingerprintB}（主键不是确定性派生的）");
        }

        if (Value(run.Output, "products") != Value(run.Output, "spec_products")
            || Value(run.Output, "variants") != Value(run.Output, "spec_variants"))
        {
            problems.Add($"行数与规格不符：products={Value(run.Output, "products")} / variants={Value(run.Output, "variants")}");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0 ? $"新库指纹与首个库一致（{fingerprintB}）" : string.Join("; ", problems)));
    }

    // ---------------------------------------------------------------------
    // 2) 指纹与"造满了"的判据必须对数据变化敏感（否则它们天生为绿 = 没有信息）
    // ---------------------------------------------------------------------
    private static void CaseFingerprintAndCompletenessCanGoRed(
        string dbPath,
        string healthyFingerprint,
        List<CaseResult> results)
    {
        const string name = "fingerprint_and_completeness_can_go_red";

        if (string.IsNullOrEmpty(healthyFingerprint))
        {
            results.Add(new CaseResult(name, false, "前置用例未产出指纹，本条无法判定（不允许静默跳过）"));
            return;
        }

        // 删一行变体：商品仍在（slug 一致 → 幂等跳过），所以行数必然少于规格。
        try
        {
            var options = new DbContextOptionsBuilder<StoreDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using var db = new StoreDbContext(options);
            db.Database.ExecuteSqlRaw("DELETE FROM ProductVariant WHERE Id = (SELECT Id FROM ProductVariant ORDER BY Id LIMIT 1);");
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
        {
            results.Add(new CaseResult(name, false, $"无法制造缺行场景（{ex.GetType().Name}）：{ex.Message}"));
            return;
        }

        var run = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(dbPath, Tier, selfTest: false));
        var problems = new List<string>();

        if (run.Code != SeedBenchCommand.ExitInconsistent)
        {
            problems.Add($"少了一个变体却仍是 rc={run.Code}（应为 {SeedBenchCommand.ExitInconsistent}）");
        }

        if (!run.Output.Contains("CHECK FAILED: ProductVariant 行数", StringComparison.Ordinal))
        {
            problems.Add("没有打印 `CHECK FAILED: ProductVariant 行数`（红在别的断言上就不算抓到）");
        }

        var mutatedFingerprint = Value(run.Output, "fingerprint");
        if (string.Equals(mutatedFingerprint, healthyFingerprint, StringComparison.Ordinal))
        {
            problems.Add($"数据变了指纹却没变（{mutatedFingerprint}）——指纹对内容不敏感，等于没用");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0
                ? $"删 1 行变体 → rc=1 且指纹从 {healthyFingerprint} 变为 {mutatedFingerprint}"
                : string.Join("; ", problems)));
    }

    // ---------------------------------------------------------------------
    // 3) 缺 --db → rc=2
    // ---------------------------------------------------------------------
    private static void CaseMissingDb(List<CaseResult> results)
    {
        const string name = "missing_db_exit_2";
        var run = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(null, Tier, selfTest: false));

        var problems = new List<string>();
        if (run.Code != SeedBenchCommand.ExitUsage)
        {
            problems.Add($"rc={run.Code}（应为 {SeedBenchCommand.ExitUsage}）");
        }

        if (!run.Output.Contains("--db", StringComparison.Ordinal))
        {
            problems.Add("没有指出缺的是 --db");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0 ? "缺 --db → rc=2 并指出参数名" : string.Join("; ", problems)));
    }

    // ---------------------------------------------------------------------
    // 4) --db 指向 openstore.dev.db → rc=2、打印原因、且不留下任何文件
    // ---------------------------------------------------------------------
    private static void CaseProtectedFileName(string tempRoot, List<CaseResult> results)
    {
        const string name = "protected_dev_db_exit_2";
        var devPath = Path.Combine(tempRoot, ProtectedFileNameLiteral);

        var run = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(devPath, Tier, selfTest: false));

        var problems = new List<string>();
        if (run.Code != SeedBenchCommand.ExitUsage)
        {
            problems.Add($"rc={run.Code}（应为 {SeedBenchCommand.ExitUsage}）");
        }

        if (!run.Output.Contains("拒绝", StringComparison.Ordinal))
        {
            problems.Add("没有打印拒绝原因（拒绝而不说明原因 = 让人去猜规则）");
        }

        if (!run.Output.Contains(ProtectedFileNameLiteral, StringComparison.Ordinal))
        {
            problems.Add($"拒绝原因里没有点名 {ProtectedFileNameLiteral}");
        }

        // 拒绝必须发生在产生副作用之前：一旦建了库，"拒绝"就只是事后通知。
        if (File.Exists(devPath))
        {
            problems.Add("拒绝的同时仍然创建了目标库文件（副作用先于校验）");
        }

        // 第二道闸门（命中 appsettings 里的绝对路径、但文件名不叫 openstore.dev.db）：
        // 没有这条断言，第二道闸门就是死代码——它从未被证明能拦住任何东西。
        var renamedByConfig = Path.Combine(tempRoot, "seeded-by-config.db");
        var renamedRefusal = BenchDataTarget.Validate(renamedByConfig, new[] { Path.GetFullPath(renamedByConfig) });
        if (renamedRefusal is null || !renamedRefusal.Contains("appsettings", StringComparison.Ordinal))
        {
            problems.Add("路径命中受保护集合时没有拒绝（或没说明是 appsettings 配的那个库）");
        }

        // 反向断言：合法路径必须被放行。否则"一律拒绝"也能让上面两条全绿。
        var legit = Path.Combine(tempRoot, "legit-bench.db");
        if (BenchDataTarget.Validate(legit, Array.Empty<string>()) is not null)
        {
            problems.Add("合法路径被拒绝（校验写成了「一律拒绝」）");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0 ? $"命名 {ProtectedFileNameLiteral} → rc=2 且未创建文件" : string.Join("; ", problems)));
    }

    // ---------------------------------------------------------------------
    // 5) tier 非法 → rc=2（且不建库）
    // ---------------------------------------------------------------------
    private static void CaseInvalidTier(string tempRoot, List<CaseResult> results)
    {
        const string name = "invalid_tier_exit_2";
        var dbPath = Path.Combine(tempRoot, "invalid-tier.db");

        var run = SeedBenchCommand.Capture(() => SeedBenchCommand.Run(dbPath, "huge", selfTest: false));

        var problems = new List<string>();
        if (run.Code != SeedBenchCommand.ExitUsage)
        {
            problems.Add($"rc={run.Code}（应为 {SeedBenchCommand.ExitUsage}）");
        }

        if (!run.Output.Contains("small|standard", StringComparison.Ordinal))
        {
            problems.Add("没有说明合法取值（small|standard）");
        }

        if (File.Exists(dbPath))
        {
            problems.Add("tier 非法却仍然创建了目标库文件");
        }

        results.Add(new CaseResult(
            name,
            problems.Count == 0,
            problems.Count == 0 ? "tier=huge → rc=2 且未创建文件" : string.Join("; ", problems)));
    }

    // ---------------------------------------------------------------------
    // 6) 门禁命令仍是"外部审查者"：seed-bench 引入对 Infrastructure 的引用后，
    //    必须保证 check-templates / check-themes 没有被顺手接上被测代码。
    // ---------------------------------------------------------------------
    private static void CaseGateCommandsStayExternal(string? repoRoot, List<CaseResult> results)
    {
        const string name = "gate_commands_stay_external";

        if (repoRoot is null)
        {
            results.Add(new CaseResult(name, false, "定位不到仓库根（含 OpenStore.sln）；不允许静默跳过"));
            return;
        }

        var toolsDir = Path.Combine(repoRoot, "tools", "OpenStore.Tools");
        if (!Directory.Exists(toolsDir))
        {
            results.Add(new CaseResult(name, false, $"目录不存在：{toolsDir}"));
            return;
        }

        var exempt = ExternalReviewerExemptFiles
            .Select(relative => Path.Combine(toolsDir, relative))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(toolsDir, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsBuildArtifact(path))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (exempt.Contains(file))
            {
                continue;
            }

            var text = StripComments(File.ReadAllText(file));
            foreach (var ns in ForbiddenNamespaces)
            {
                if (text.Contains(ns, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(repoRoot, file)} → {ns}");
                }
            }
        }

        results.Add(new CaseResult(
            name,
            offenders.Count == 0,
            offenders.Count == 0
                ? $"check-* 命令与 Validation/ 未引用 {string.Join("/", ForbiddenNamespaces.Select(n => n.Replace("OpenStore.", string.Empty, StringComparison.Ordinal)))}（只有 seed-bench 例外）"
                : $"门禁命令被接上了被测代码：{string.Join("; ", offenders)}"));
    }

    // ---------------------------------------------------------------------
    // 工具
    // ---------------------------------------------------------------------
    private static int Report(List<CaseResult> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            Console.WriteLine($"  [{i + 1}] {(result.Ok ? "ok  " : "FAIL")} {result.Name}");
            Console.WriteLine($"       {result.Detail.Replace("\n", "\n       ", StringComparison.Ordinal)}");
        }

        var failed = results.Where(r => !r.Ok).ToList();
        if (failed.Count == 0)
        {
            Console.WriteLine($"== 自证通过：{results.Count} 项（{string.Join(", ", results.Select(r => r.Name))}）");
            return SeedBenchCommand.ExitOk;
        }

        Console.WriteLine($"== 自证失败：{failed.Count} 项不符合预期（{string.Join(", ", failed.Select(r => r.Name))}）");
        Console.WriteLine("GATE FAILED: seed_bench_self_test");
        return SeedBenchCommand.ExitInconsistent;
    }

    private static string? Value(string output, string key)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return trimmed[(key.Length + 1)..].Trim();
            }
        }

        return null;
    }

    private static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(fullRoot, StringComparison.Ordinal)
               || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
               || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败只影响"临时文件残留"，不影响自证结论；但要让使用者知道该删哪儿。
            Console.Error.WriteLine($"seed-bench self-test: 临时目录未清理干净，请手工删除 {directory}（{ex.GetType().Name}）");
        }
    }

    /// <summary>剥掉注释：判断"有没有引用某命名空间"时，注释里的说明不算违规。</summary>
    private static string StripComments(string source)
    {
        var output = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var current = source[i];

            if (current == '/' && Peek(source, i + 1) == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (current == '/' && Peek(source, i + 1) == '*')
            {
                i += 2;
                while (i < source.Length && !(source[i] == '*' && Peek(source, i + 1) == '/'))
                {
                    if (source[i] == '\n')
                    {
                        output.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(i + 2, source.Length);
                continue;
            }

            if (current is '"' or '\'')
            {
                var quote = current;
                output.Append(current);
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    output.Append(source[i]);
                    if (source[i] == quote)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                continue;
            }

            output.Append(current);
            i++;
        }

        return output.ToString();
    }

    private static char Peek(string source, int index) => index < source.Length ? source[index] : '\0';

    private readonly record struct CaseResult(string Name, bool Ok, string Detail);
}
