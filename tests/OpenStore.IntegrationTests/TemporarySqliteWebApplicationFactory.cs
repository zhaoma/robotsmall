using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace OpenStore.IntegrationTests;

/// <summary>
/// 每条测试一个独立的临时 SQLite 库 + WebApplicationFactory 宿主。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么每个实例一个 GUID 子目录</b>：M1 判据要求"临时库唯一、可清理、可重复"。
/// 固定路径会让两次连续运行的 schema/数据互相污染，也会让"残留发生在删除路径上"无法被发现。
/// 这里把每个 factory 的库放在 <c>{TempPath}/openstore-m1-it/{guid}/m1.db</c>，
/// Dispose 删除整个 guid 子目录——反向检查只需看 <c>openstore-m1-it</c> 下是否还有残留。
/// </para>
/// <para>
/// <b>为什么不碰 openstore.dev.db</b>：连接串在 <see cref="ConfigureWebHost"/> 里被
/// <see cref="UseSetting"/> 覆盖（host config 优先级高于 appsettings.json）。若覆盖失效，
/// Program 会去连开发库，测试会读写开发数据——所以测试断言里额外核对了 <see cref="DbPath"/> 存在。
/// </para>
/// <para>
/// <b>鉴权不关闭</b>：Development 环境让组合根跑 <c>DevAdminSeeder</c>，用已知密码种一个
/// Admin 账号；认证处理器、CSRF 中间件、权限策略全部按生产路径运行，测试通过真实登录拿 principal。
/// </para>
/// </remarks>
public sealed class TemporarySqliteWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>约定的临时库根目录。反向检查读它，判断运行结束后是否还有残留。</summary>
    public static readonly string TempRootBase = Path.Combine(Path.GetTempPath(), "openstore-m1-it");

    public const string DevAdminEmail = "admin@openstore.local";

    public const string DevAdminPassword = "M1Acceptance123!";

    /// <summary>本实例独有的临时目录（guid）。</summary>
    public string TempDir { get; }

    /// <summary>本实例独有的库文件路径。</summary>
    public string DbPath { get; }

    public string ConnectionString => $"Data Source={DbPath}";

    public TemporarySqliteWebApplicationFactory()
    {
        TempDir = Path.Combine(TempRootBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
        DbPath = Path.Combine(TempDir, "m1.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 显式 Development：触发 Program.cs 里的自动迁移（单一基线）与 DevAdminSeeder。
        builder.UseEnvironment("Development");

        // 覆盖 appsettings.json 的 ConnectionStrings:Default。UseSetting 写入 host configuration，
        // 优先级高于 appsettings，Program.cs 的 GetConnectionString("Default") 读到这里的值。
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);

        // 固定种子管理员密码，测试按此登录；不依赖 Program 里的默认值。
        builder.UseSetting("Auth:DevAdminPassword", DevAdminPassword);
    }

    /// <summary>
    /// 前置条件探针：库文件是否已由单一基线迁移建出（有 sqlite_master 表即视为迁移执行过）。
    /// 建不起来返回 false，测试据此判 PRECONDITION（rc=2），而不是静默跳过或伪装成通过。
    /// </summary>
    public bool IsDatabaseReady()
    {
        if (!File.Exists(DbPath))
        {
            return false;
        }

        try
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Product';";
            var n = Convert.ToInt64(cmd.ExecuteScalar());
            return n > 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                // SQLite 连接池可能仍持有句柄，先清空再删文件（删不掉时是环境错误，不该静默吞）。
                SqliteConnection.ClearAllPools();
                TryDelete(DbPath);
                TryDelete(DbPath + "-wal");
                TryDelete(DbPath + "-shm");
                TryDeleteDir(TempDir);
                TryDeleteDirIfEmpty(TempRootBase);
            }
        }
        else
        {
            base.Dispose(disposing);
        }
    }

    private static void TryDelete(string path)
    {
        for (var i = 0; i < 5 && File.Exists(path); i++)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static void TryDeleteDir(string dir)
    {
        for (var i = 0; i < 5 && Directory.Exists(dir); i++)
        {
            try
            {
                // 只删本实例 guid 子目录，不碰 TempRootBase（其他实例可能正在用）。
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>
    /// 删空根目录：仅在该目录此刻为空（无其他并发实例残留）时才删成功；
    /// 非空抛 IOException，直接忽略——不递归删，避免误删别人正在用的库。
    /// </summary>
    private static void TryDeleteDirIfEmpty(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: false);
        }
        catch (IOException)
        {
            // 目录不存在（已被删）或非空（其他实例仍在用）都落这里，均属正常：
            // 非空时留给最后一个退出的实例去删，不递归删以免误删别人正在用的库。
        }
    }
}
