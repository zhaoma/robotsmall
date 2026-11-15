using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// 设计期 DbContext 工厂：<c>dotnet ef</c> 能跑起来的前提。
/// </summary>
/// <remarks>
/// <para>
/// 迁移工具需要一个<b>不需要启动 Web 宿主</b>的构造路径。如果没有这个工厂，EF 会尝试去
/// 启动 Api 项目的 <c>Program</c>（还会跑 DI、读配置、连数据库），
/// 导致"生成迁移"这种纯离线操作被迫依赖运行时环境（门禁步骤 8 就是这样变脆的）。
/// </para>
/// <para>
/// 这里的连接串<b>故意写死开发库相对路径</b>：迁移只比较模型，不读数据。
/// 生产迁移用 <c>dotnet ef migrations script</c> 产出 SQL 由运维执行（部署文档 §5）。
/// </para>
/// </remarks>
public sealed class StoreDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    /// <summary>开发库连接串，契约固定（devops 的 check.sh 步骤 8 直接调本项目）。</summary>
    public const string DevelopmentConnectionString = "Data Source=./openstore.dev.db";

    public StoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(DevelopmentConnectionString)
            .Options;

        return new StoreDbContext(options);
    }
}
