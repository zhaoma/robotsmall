using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenStore.Domain.Catalog;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// AC-12 基准数据集的生成逻辑（10,000 商品 / 50,000 变体 / 每分类 500 商品）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么写在这里而不是一个独立的 SQL 脚本</b>：种子数据必须走
/// <see cref="Product"/> 聚合 + <see cref="ProductVariant"/> 的构造函数，让不变量（slug 形态、
/// 轴签名唯一、Active 必须有价）真的被执行一次。裸 <c>INSERT</c> 造得出来的数据，
/// 代码可能认为是非法的——那样压测压的是"一份库里有、代码不认"的数据，得出的
/// TTFB 与内存结论没有意义（数据一进领域层就抛不变量异常，压测根本走不到渲染）。
/// </para>
/// <para>
/// <b>确定性</b>：主键由种子字符串（商品序号 / SKU）经 SHA-256 派生，不用
/// <c>Guid.NewGuid()</c>。同一个 tier 跑两次，所有表行数与指纹必须完全一致——
/// 否则 soak 报告里的"压的是同一批数据"这句话就无从证明（见 <see cref="ComputeFingerprintAsync"/>）。
/// </para>
/// <para>
/// <b>幂等</b>：按 <see cref="Product.Slug"/> 判重（slug 是商品的业务主键，且已由
/// 数据模型的唯一索引保证），已存在的商品整条跳过。重复跑不重复插入、不报错，
/// 与 docs/02 的 fixture 幂等约定同一口径（"按 Sku / 固定 Id 判重"）。
/// </para>
/// </remarks>
public static class BenchDataSeeder
{
    /// <summary>基准商品的 slug 前缀。存在的行数靠它统计，改它等于改"造满了"的判据。</summary>
    public const string SlugPrefix = "bench-c";

    /// <summary>基准变体的 SKU 前缀（全大写，SKU 守卫要求首字符为 <c>[A-Z0-9]</c>）。</summary>
    public const string SkuPrefix = "BENCH-";

    /// <summary>数据集版本。指纹必须与它绑定：改了生成规则却没改版本号，历史报告会静默失真。</summary>
    public const string SeedVersion = "bench-v1";

    /// <summary>每批 <c>SaveChanges</c> 的商品数。50,000 变体一次 <c>SaveChanges</c> 会吃掉几百 MB，
    /// 而 bench 本身就在 2GB 机器上跑——造数不该成为 OOM 的第一个受害者。</summary>
    private const int BatchSize = 250;

    private static readonly string[] GradeNames = { "标准型", "工业型", "精密型", "加固型", "经济型" };

    private static readonly string[] SizeCodes = { "s", "m", "l", "xl", "xxl" };

    private static readonly string[] ColorCodes = { "black", "white", "red", "blue", "gray" };

    /// <summary>分类骨架：20 个分类、3 级（2 根 + 6 中 + 12 叶）。20 能被 10,000 整除 → 每分类恰好 500。</summary>
    private static readonly BenchCategory[] Shape =
    {
        new(0, 1, null, "robot-parts", "机器人配件"),
        new(1, 1, null, "automation", "自动化产线"),
        new(2, 2, 0, "sensors", "传感器"),
        new(3, 2, 0, "actuators", "执行机构"),
        new(4, 2, 0, "controllers", "控制器"),
        new(5, 2, 1, "conveyors", "输送设备"),
        new(6, 2, 1, "grippers", "夹爪与工装"),
        new(7, 2, 1, "vision", "视觉系统"),
        new(8, 3, 2, "laser-sensors", "激光传感器"),
        new(9, 3, 2, "proximity-sensors", "接近传感器"),
        new(10, 3, 3, "servo-motors", "伺服电机"),
        new(11, 3, 3, "linear-actuators", "直线执行器"),
        new(12, 3, 4, "motion-controllers", "运动控制器"),
        new(13, 3, 4, "plc-units", "PLC 单元"),
        new(14, 3, 5, "belt-conveyors", "皮带输送线"),
        new(15, 3, 5, "roller-conveyors", "滚筒输送线"),
        new(16, 3, 6, "pneumatic-grippers", "气动夹爪"),
        new(17, 3, 6, "electric-grippers", "电动夹爪"),
        new(18, 3, 7, "machine-vision-cameras", "工业相机"),
        new(19, 3, 7, "vision-lighting", "视觉光源"),
    };

    /// <summary>两档规格。<c>small</c> 是每次改动都要跑的档，<c>standard</c> 是 AC-12 的验收档。</summary>
    public static BenchSpec SmallSpec { get; } = BenchSpec.Create("small", 1_000, 5, Shape);

    public static BenchSpec StandardSpec { get; } = BenchSpec.Create("standard", 10_000, 5, Shape);

    public static BenchSpec? FindSpec(string? tier)
        => tier switch
        {
            null or "" => SmallSpec,
            "small" => SmallSpec,
            "standard" => StandardSpec,
            _ => null,
        };

    /// <summary>
    /// 按规格把数据灌进目标库。<b>不负责</b>目标库是否安全（那是调用方的前置条件，
    /// 见 <see cref="BenchDataTarget"/>），也不打印任何东西（打印是 CLI 的事）。
    /// </summary>
    public static async Task<BenchSeedReport> SeedAsync(
        StoreDbContext db,
        BenchSpec spec,
        Action<string>? log,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(spec);

        // 1) 建表：走迁移而不是 EnsureCreated。用 EnsureCreated 建出来的库没有
        //    __EFMigrationsHistory，Api 启动时的 AutoMigrate 会试图再跑一次 InitialCreate
        //    并因"表已存在"失败——于是压测连站都起不来，而报错信息看起来像数据库损坏。
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        // 2) WAL：AC-12 的运行规格是「SQLite（WAL）」。不设的话读写互斥，压测量到的是
        //    rollback journal 的锁竞争，而不是被测代码的表现。
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);

        // 3) 判重集：只取 slug 一列（50,000 行变体不进内存），已存在的商品整条跳过。
        var existingSlugs = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug.StartsWith(SlugPrefix))
            .Select(p => p.Slug)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var seen = new HashSet<string>(existingSlugs, StringComparer.Ordinal);

        log?.Invoke($"已有基准商品 {seen.Count} 个（按 slug 判重，重复的跳过）");

        var insertedProducts = 0;
        var insertedVariants = 0;

        for (var index = 0; index < spec.ProductCount; index += BatchSize)
        {
            var batch = new List<ProductRecord>(Math.Min(BatchSize, spec.ProductCount - index));
            var batchVariants = 0;

            for (var i = index; i < index + BatchSize && i < spec.ProductCount; i++)
            {
                var slug = BuildSlug(i, spec);
                if (!seen.Add(slug))
                {
                    continue; // 幂等：这一条已经在了
                }

                var record = BuildProduct(i, spec);
                batch.Add(record);
                batchVariants += record.Variants.Count;
            }

            if (batch.Count > 0)
            {
                db.Products.AddRange(batch);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                insertedProducts += batch.Count;
                insertedVariants += batchVariants;

                // 跟踪 60,000 个实体会让造数自己吃掉几百 MB —— 每批清一次，内存曲线才是平的
                db.ChangeTracker.Clear();
            }

            if ((index / BatchSize) % 4 == 0)
            {
                log?.Invoke($"  进度 {Math.Min(index + BatchSize, spec.ProductCount)}/{spec.ProductCount}");
            }
        }

        var verification = await VerifyAsync(db, spec, ct).ConfigureAwait(false);
        var tables = await ComputeFingerprintAsync(db, ct).ConfigureAwait(false);
        var fingerprint = CombineFingerprint(tables);

        return new BenchSeedReport(
            spec,
            insertedProducts,
            insertedVariants,
            spec.ProductCount - insertedProducts,
            fingerprint,
            tables,
            verification);
    }

    /// <summary>
    /// "造满了"的判据：行数必须等于规格，每个分类必须恰好 <c>ProductsPerCategory</c> 个商品。
    /// </summary>
    /// <remarks>
    /// <b>为什么这条必须在生成器里而不是只在压测脚本里</b>：docs/06 §4.1 的原话是
    /// "造不满就等于没测"。少了 3,000 个商品，TTFB 仍会漂亮地绿——因为负载轻了。
    /// 判据必须紧挨着造数，才能在出报告之前就把"半套数据"拦下来。
    /// </remarks>
    public static async Task<BenchVerification> VerifyAsync(StoreDbContext db, BenchSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(spec);

        var problems = new List<string>();

        var products = await db.Products
            .AsNoTracking()
            .CountAsync(p => p.Slug.StartsWith(SlugPrefix), ct)
            .ConfigureAwait(false);

        var variants = await db.ProductVariants
            .AsNoTracking()
            .CountAsync(v => v.Sku.StartsWith(SkuPrefix), ct)
            .ConfigureAwait(false);

        const int activeStatus = (int)ProductStatus.Active;
        var active = await db.Products
            .AsNoTracking()
            .CountAsync(p => p.Slug.StartsWith(SlugPrefix) && p.Status == activeStatus, ct)
            .ConfigureAwait(false);

        if (products != spec.ProductCount)
        {
            problems.Add($"Product 行数 {products} != 规格 {spec.ProductCount}");
        }

        if (variants != spec.VariantCount)
        {
            problems.Add($"ProductVariant 行数 {variants} != 规格 {spec.VariantCount}");
        }

        // 每个商品必须**恰好** VariantsPerProduct 个变体。
        // 光看总数不够：一个商品挂 10 个、另一个挂 0 个，总数照样对，而前台的规格选择器
        // 与"每商品 5 个 SKU"的假设已经破了——这类数据会让商品详情页的变体矩阵
        // 在某些商品上返回空，且只在被点开时才暴露。
        var unevenProducts = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug.StartsWith(SlugPrefix))
            .Select(p => new { p.Id, Count = p.Variants.Count })
            .CountAsync(x => x.Count != spec.VariantsPerProduct, ct)
            .ConfigureAwait(false);

        if (unevenProducts != 0)
        {
            problems.Add($"{unevenProducts} 个商品的变体数 != {spec.VariantsPerProduct}");
        }

        // 同商品内轴签名重复 = 0。
        // DB 上已有唯一索引 IX_ProductVariant_Product_AxisSignature，所以这条**不是**
        // 重复检查领域层已经做过的事：它验的是"那个索引真的在这份库里"。
        // 手写迁移、重建表（SQLite 改表要重建）都可能把索引丢掉，而丢掉之后
        // 现象是"同一组合出现两个可售变体"——下单时才知道。
        var duplicateAxis = await db.ProductVariants
            .AsNoTracking()
            .Where(v => v.Sku.StartsWith(SkuPrefix))
            .GroupBy(v => new { v.ProductId, v.AxisSignature })
            .Where(g => g.Count() > 1)
            .CountAsync(ct)
            .ConfigureAwait(false);

        if (duplicateAxis != 0)
        {
            problems.Add($"{duplicateAxis} 组 (ProductId, AxisSignature) 重复（唯一索引没生效）");
        }

        // 分类分布：M0 物理表还没有 Category / ProductCategory（M1 才落），
        // 所以分类归属暂由 slug 的 `bench-cNN-` 段表达。
        //
        // ⚠️ **这只证明"列表数据量"，不证明"分类 join 成本"。** 报告里必须成对出现，
        // 否则"每分类 500 ✅"会被当成"分类页指标已达标"用（见 docs/06 §4.1 的缺口表）。
        //
        // ⚠️ **M1 必须迁移判据，不要留 LIKE 当长期判据**：目标模型里 `Product` 没有 slug 列，
        // slug 在 ProductTranslation 上且唯一索引是 (Locale, Slug) —— 同一个商品在不同语种
        // 可以有不同 slug。所以 slug 段只在**默认语种那一行**成立，拿它做跨语种分类 key 会错；
        // 而分类重新归属时 slug 不会变，LIKE 会静默给出**错**的分类，那恰恰是 AC-12 要量的东西。
        // 迁移形态：先建 Category，再为每个商品建恰好一行 ProductCategory(IsPrimary=true)，
        // 此处改为 join 计数；slug 段只作一次性回填依据。
        for (var c = 0; c < spec.Categories.Count; c++)
        {
            var prefix = CategorySlugPrefix(c);
            var count = await db.Products
                .AsNoTracking()
                .CountAsync(p => p.Slug.StartsWith(prefix), ct)
                .ConfigureAwait(false);

            if (count != spec.ProductsPerCategory)
            {
                problems.Add($"分类 c{c:D2}({prefix}) 商品数 {count} != 规格 {spec.ProductsPerCategory}");
            }
        }

        return new BenchVerification(products, variants, active, products - active, unevenProducts, duplicateAxis, problems);
    }

    /// <summary>
    /// 规格与数据量指纹：每张表取全部 Id（排序后拼成文本）算 sha256，再把这些
    /// <c>表名:行数:摘要</c> 行合并算一次总指纹。
    /// </summary>
    /// <remarks>
    /// <b>为什么要指纹而不只是行数</b>：行数相同的两份数据可以完全不同（换了一批商品、
    /// 变体挂错了父商品）。压测报告必须能证明"这一轮压的和上一轮是同一批数据"，
    /// 否则两次结论不可比，也无法排除"数据变了"这个解释。
    /// 取前 16 位（64 bit）够防 accidental mismatch，且能贴进一行表格。
    /// </remarks>
    public static async Task<IReadOnlyList<BenchTableStat>> ComputeFingerprintAsync(StoreDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var productIds = await db.Products.AsNoTracking().Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        var variantIds = await db.ProductVariants.AsNoTracking().Select(v => v.Id).ToListAsync(ct).ConfigureAwait(false);
        var adminIds = await db.AdminUsers.AsNoTracking().Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false);

        // Session 的主键是 TokenHash（string），不是 Id —— 这里按"表的主键列"取，不硬套 Guid。
        var sessionKeys = await db.Sessions.AsNoTracking().Select(s => s.TokenHash).ToListAsync(ct).ConfigureAwait(false);

        return new[]
        {
            Stat("Product", productIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture))),
            Stat("ProductVariant", variantIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture))),
            Stat("AdminUser", adminIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture))),
            Stat("Session", sessionKeys),
        };
    }

    /// <summary>把每张表的摘要合并成总指纹（前 16 位十六进制，小写）。</summary>
    public static string CombineFingerprint(IReadOnlyList<BenchTableStat> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var material = string.Join(
            '\n',
            tables
                .OrderBy(t => t.Table, StringComparer.Ordinal)
                .Select(t => $"{t.Table}:{t.Rows}:{t.Sha256}"));

        return Hash16(material);
    }

    /// <summary>建一张表：<c>product</c> 序号 → slug。同时是判重键与分类归属的唯一来源。</summary>
    public static string BuildSlug(int index, BenchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return $"{CategorySlugPrefix(index % spec.Categories.Count)}p{index.ToString("D5", CultureInfo.InvariantCulture)}";
    }

    private static string CategorySlugPrefix(int categoryIndex)
        => $"{SlugPrefix}{categoryIndex.ToString("D2", CultureInfo.InvariantCulture)}-";

    private static ProductRecord BuildProduct(int index, BenchSpec spec)
    {
        var category = spec.Categories[index % spec.Categories.Count];
        var slug = BuildSlug(index, spec);
        var title = $"{category.DisplayName} 基准商品 {index.ToString("D5", CultureInfo.InvariantCulture)} · {GradeNames[index % GradeNames.Length]}";

        // 轴按分类奇偶分流：偶数分类用尺码、奇数分类用颜色。这样 facet 筛选有两个真实维度，
        // 而不是"所有商品都只有一种轴"——后者会让筛选路径测出来偏乐观。
        var useColor = category.Index % 2 == 1;
        var optionCodes = useColor ? ColorCodes : SizeCodes;
        var axisKey = useColor ? "color" : "size";

        var variants = new List<ProductVariant>(spec.VariantsPerProduct);
        for (var v = 0; v < spec.VariantsPerProduct; v++)
        {
            var option = optionCodes[v % optionCodes.Length];
            var axes = new Dictionary<string, string>(StringComparer.Ordinal) { [axisKey] = option };
            var prices = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                // 19.99 起、按商品序号阶梯上浮，变体再加价：价格区间要铺开，
                // 否则"排序 / 区间筛选"这类查询永远走同一条最优路径，测不出退化。
                ["CNY"] = 1999L + (index % 300) * 100L + v * 250L,
            };

            variants.Add(new ProductVariant(
                DeterministicGuid($"bench-variant:{index}:{v}"),
                $"{SkuPrefix}C{category.Index.ToString("D2", CultureInfo.InvariantCulture)}-" +
                $"P{index.ToString("D5", CultureInfo.InvariantCulture)}-{option.ToUpperInvariant()}",
                VariantMatrix.BuildSignature(axes),
                prices));
        }

        var product = Product.Create(DeterministicGuid($"bench-product:{index}"), slug, title, variants);

        // 2.7% 的草稿商品（37 与 20 互素，草稿会均匀散到 20 个分类里，不会全挤在 c00）。
        // 混合状态是必要的：admin 列表要能看到草稿，前台只能看到在售。
        if (index % 37 != 0)
        {
            product.ChangeStatus(ProductStatus.Active);
        }

        var record = new ProductRecord();
        ProductMapping.ApplyScalars(product, record);
        record.Variants.AddRange(product.Variants.Select(v => ProductMapping.ToRecord(v, product.Id)));
        return record;
    }

    /// <summary>
    /// 由名字派生稳定的 <see cref="Guid"/>（sha256 前 16 字节）。
    /// 换掉 <c>Guid.NewGuid()</c> 是为了让"同一个 tier 跑两次 → 同一份数据"成立；
    /// 顺带让压测报告里的商品 Id 可以在两次运行之间对齐。
    /// </summary>
    public static Guid DeterministicGuid(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);
        return new Guid(hash[..16]);
    }

    private static BenchTableStat Stat(string table, IEnumerable<string> keys)
    {
        var ordered = keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        return new BenchTableStat(table, ordered.Count, Hash16(string.Join('\n', ordered)));
    }

    /// <summary>
    /// 取 sha256 的**前 16 位十六进制**（64 bit）。够防 accidental mismatch，又能贴进一行验收记录。
    /// 注意别写成"前 16 字节"——那会输出 32 个字符，而验收模板与自证断言都按 16 字符写。
    /// </summary>
    private static string Hash16(string material)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}

/// <summary>一个分类。<see cref="Index"/> 决定 slug 段，<see cref="ParentIndex"/> 表达 3 级结构。</summary>
public sealed record BenchCategory(int Index, int Depth, int? ParentIndex, string Key, string DisplayName);

/// <summary>一档数据规格。构造时校验"能被分类数整除"，否则"每分类 N 个"这句话不成立。</summary>
public sealed record BenchSpec
{
    private BenchSpec(string name, int productCount, int variantsPerProduct, IReadOnlyList<BenchCategory> categories)
    {
        Name = name;
        ProductCount = productCount;
        VariantsPerProduct = variantsPerProduct;
        Categories = categories;
    }

    public string Name { get; }

    public int ProductCount { get; }

    public int VariantsPerProduct { get; }

    public IReadOnlyList<BenchCategory> Categories { get; }

    public int CategoryCount => Categories.Count;

    public int VariantCount => ProductCount * VariantsPerProduct;

    public int ProductsPerCategory => ProductCount / CategoryCount;

    public static BenchSpec Create(string name, int productCount, int variantsPerProduct, IReadOnlyList<BenchCategory> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(categories);

        if (productCount <= 0 || variantsPerProduct <= 0 || categories.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productCount), "spec counts must be positive");
        }

        if (productCount % categories.Count != 0)
        {
            throw new ArgumentException(
                $"{productCount} products cannot be split evenly across {categories.Count} categories; " +
                "\"每分类 N 个商品\" would be a false statement about the dataset.",
                nameof(productCount));
        }

        return new BenchSpec(name, productCount, variantsPerProduct, categories);
    }

    public string Describe()
        => $"{ProductCount} 商品 / {VariantCount} 变体 / {CategoryCount} 分类 / 每分类 {ProductsPerCategory}";
}

/// <summary>单表指纹。行数与摘要必须一起出现：只有摘要看不出"哪张表少了"。  </summary>
public sealed record BenchTableStat(string Table, int Rows, string Sha256);

/// <summary>"造满了"的核对结果。<see cref="Problems"/> 为空才算通过。</summary>
/// <remarks>
/// <see cref="UnevenProducts"/> 与 <see cref="DuplicateAxisSignatures"/> 单独暴露、不合并进
/// <see cref="Problems"/> 的计数里：报告要能看出"总数达标但分布坏了"这种形态。
/// 行数对、分布不对，是最容易被读成"通过"的一类失败。
/// </remarks>
public sealed record BenchVerification(
    int Products,
    int Variants,
    int Active,
    int Draft,
    int UnevenProducts,
    int DuplicateAxisSignatures,
    IReadOnlyList<string> Problems)
{
    public bool IsComplete => Problems.Count == 0;
}

/// <summary>一次造数的完整结果，供 CLI 打印。</summary>
public sealed record BenchSeedReport(
    BenchSpec Spec,
    int InsertedProducts,
    int InsertedVariants,
    int SkippedProducts,
    string Fingerprint,
    IReadOnlyList<BenchTableStat> Tables,
    BenchVerification Verification);
