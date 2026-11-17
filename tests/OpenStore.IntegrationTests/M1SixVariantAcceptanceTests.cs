using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenStore.Application.Catalog.Dtos.Admin;
using OpenStore.Application.Catalog.Dtos.Storefront;
using OpenStore.Domain.Catalog;
using Xunit.Abstractions;

namespace OpenStore.IntegrationTests;

/// <summary>
/// M1 完成判据的机器载体：一个测试方法内完成「6 变体建品 → 上架 → 前台最低正价断言」全链路。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不关鉴权</b>：登录走 <c>/api/admin/auth/login</c>，拿到 session cookie 与 CSRF cookie，
/// 后续写请求带 Origin + X-CSRF-Token + Cookie。认证处理器、权限策略、CSRF 中间件全部按生产路径运行。
/// </para>
/// <para>
/// <b>样本唯一出处</b>：<c>fixtures/m1-variant-acceptance.json</c>（复制自 tests/fixtures）。
/// 最低正价从文件算 <c>min(amountMinor)</c>，不硬编码 1999——那是第二份样本。
/// </para>
/// </remarks>
public sealed class M1SixVariantAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITestOutputHelper _output;

    public M1SixVariantAcceptanceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// M1 正向判据。三件事（建品 / 上架 / 前台断言）在同一方法、同一条链路里完成。
    /// </summary>
    [Fact]
    [Trait("Category", "M1")]
    public async Task M1_MinPrice_EndToEnd()
    {
        using var factory = new TemporarySqliteWebApplicationFactory();

        // CreateClient 首次触发 WAF 宿主启动 → Program.cs 自动迁移（单一基线）→ 种子管理员。
        var (client, request) = await BuildAuthenticatedClientAndFixtureAsync(factory);
        // 前置条件：库必须由单一基线迁移建出；建不起来 = PRECONDITION，不是跳过。
        EnsureDatabaseReady(factory);
        var expectedMin = request.Variants.Min(v => v.Price.AmountMinor);
        var expectedCurrency = request.Variants
            .Select(v => v.Price.Currency)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .First();

        // ---- ① POST 建品 ----
        var createResp = await client.PostAsJsonAsync("/api/admin/products", request, JsonOpts);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<AdminProductDetailDto>(JsonOpts);
        Assert.NotNull(created);
        Assert.Equal(6, created.Variants.Count);
        // SKU 唯一是真契约：Product.GuardVariants 前置守卫（PRODUCT_SKU_DUPLICATE）+
        // DB 唯一索引 UX_Variant_Sku 兜底。这里断言回显的 6 个 SKU 互不相同，
        // 验的是这条领域+DB 不变量在真实 HTTP 链路上成立（含夹具合法性）。
        Assert.Equal(6, created.Variants.Select(v => v.Sku).Distinct().Count());

        // ---- ② PATCH 上架 ----
        var statusResp = await client.PatchAsJsonAsync(
            $"/api/admin/products/{created.Id}/status",
            new ChangeProductStatusRequest(ProductStatus.Active),
            JsonOpts);
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

        // ---- ③ GET 前台最低正价 ----
        var storeResp = await client.GetAsync($"/api/v1/en/products/{request.Slug}");
        Assert.Equal(HttpStatusCode.OK, storeResp.StatusCode);
        var dto = await storeResp.Content.ReadFromJsonAsync<StorefrontProductDto>(JsonOpts);
        Assert.NotNull(dto);
        Assert.Equal(expectedCurrency, dto.Currency);
        Assert.Equal(expectedMin, dto.AmountMinor);

        _output.WriteLine($"DETAIL: M1 最低正价断言通过（6 变体 → 前台最低正价 = {dto.AmountMinor}）");
    }

    /// <summary>
    /// fail-before-pass 反向对照：走完全相同的链路拿到前台 200，然后断言一个被注入偏差的价格。
    /// 默认偏差 0（全量 dotnet test 绿）；取证时设环境变量 M1_NEGATIVE_DELTA=1，
    /// 断言 expectedMin+1，红恰好落在"最低正价"那条断言上（不是 build / HTTP / 进程失败）。
    /// 不打 Category=M1 trait，check.sh 的 --filter "Category=M1" 不会选中它。
    /// </summary>
    [Fact]
    [Trait("Category", "M1-NegativeControl")]
    public async Task M1_MinPrice_NegativeControl()
    {
        var deltaText = Environment.GetEnvironmentVariable("M1_NEGATIVE_DELTA");
        var delta = string.IsNullOrEmpty(deltaText) ? 0 : int.Parse(deltaText);

        using var factory = new TemporarySqliteWebApplicationFactory();
        var (client, request) = await BuildAuthenticatedClientAndFixtureAsync(factory);
        EnsureDatabaseReady(factory);
        var expectedMin = request.Variants.Min(v => v.Price.AmountMinor);

        var createResp = await client.PostAsJsonAsync("/api/admin/products", request, JsonOpts);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<AdminProductDetailDto>(JsonOpts);
        Assert.NotNull(created);

        var statusResp = await client.PatchAsJsonAsync(
            $"/api/admin/products/{created.Id}/status",
            new ChangeProductStatusRequest(ProductStatus.Active),
            JsonOpts);
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

        var storeResp = await client.GetAsync($"/api/v1/en/products/{request.Slug}");
        Assert.Equal(HttpStatusCode.OK, storeResp.StatusCode);
        var dto = await storeResp.Content.ReadFromJsonAsync<StorefrontProductDto>(JsonOpts);
        Assert.NotNull(dto);

        // 反向断言：注入偏差后红在这一行，证明断言确实绑在"最低正价"上。
        Assert.Equal(expectedMin + delta, dto.AmountMinor);
    }

    /// <summary>前置条件不满足时抛带 PRECONDITION 标记的错误——由调用方/门禁映射成 rc=2，不静默跳过。</summary>
    private static void EnsureDatabaseReady(TemporarySqliteWebApplicationFactory factory)
    {
        if (!factory.IsDatabaseReady())
        {
            throw new InvalidOperationException(
                $"PRECONDITION: 临时库未就绪（迁移未执行或建库失败）：{factory.DbPath}");
        }
    }

    /// <summary>真实登录并把 principal（cookie + CSRF）装进 client 的默认请求头，同时载入夹具。</summary>
    private static async Task<(HttpClient Client, CreateProductRequest Request)> BuildAuthenticatedClientAndFixtureAsync(
        TemporarySqliteWebApplicationFactory factory)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "m1-variant-acceptance.json");
        if (!File.Exists(fixturePath))
        {
            throw new InvalidOperationException($"PRECONDITION: 夹具文件缺失：{fixturePath}");
        }

        var request = JsonSerializer.Deserialize<CreateProductRequest>(File.ReadAllText(fixturePath), JsonOpts)
            ?? throw new InvalidOperationException("PRECONDITION: 夹具反序列化为 null。");

        var client = factory.CreateClient();

        // CSRF middleware 对登录也校验 Origin（WAF 默认基址 http://localhost/）。
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");

        var loginBody = new
        {
            email = TemporarySqliteWebApplicationFactory.DevAdminEmail,
            password = TemporarySqliteWebApplicationFactory.DevAdminPassword,
        };
        var loginResp = await client.PostAsJsonAsync("/api/admin/auth/login", loginBody, JsonOpts);
        if (loginResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await loginResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"PRECONDITION: 鉴权不可行——登录返回 {(int)loginResp.StatusCode} {body}");
        }

        var setCookies = loginResp.Headers.TryGetValues("Set-Cookie", out var vals)
            ? vals.ToList()
            : new List<string>();
        var sid = GetCookieValue(setCookies, "os_admin_sid");
        var csrf = GetCookieValue(setCookies, "os_admin_csrf");
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(csrf))
        {
            throw new InvalidOperationException(
                "PRECONDITION: 登录未写出 os_admin_sid / os_admin_csrf Cookie，鉴权链路不可行。");
        }

        // 后续管理端写请求：带 session cookie + CSRF cookie + double-submit 头。
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"os_admin_sid={sid}; os_admin_csrf={csrf}");
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);

        return (client, request);
    }

    private static string? GetCookieValue(IEnumerable<string> setCookies, string name)
    {
        foreach (var raw in setCookies)
        {
            var pair = raw.Split(';', 2)[0].Trim();
            var eq = pair.IndexOf('=');
            if (eq > 0 && string.Equals(pair[..eq], name, StringComparison.Ordinal))
            {
                return pair[(eq + 1)..];
            }
        }

        return null;
    }
}
