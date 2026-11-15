using Microsoft.EntityFrameworkCore;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// 唯一的 DbContext：8 个限界上下文的映射平铺在同一个 context 里（模块化单体，架构 §0）。
/// </summary>
/// <remarks>
/// <para>
/// 每实体一个显式 <c>IEntityTypeConfiguration</c>（见 <c>Configurations/</c>），
/// <b>不靠约定推断</b>：约定推断在实体变多之后会静默改表结构，是最难查的一类 PM 事故。
/// </para>
/// <para>
/// M0 只映射 Catalog 的 Product/ProductVariant。其余上下文的映射在 M1 陆续加，
/// 迁移版本号连续，禁止"一次性大迁移"（部署文档 §5 要求迁移可单步回滚）。
/// </para>
/// </remarks>
public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<Records.ProductRecord> Products => Set<Records.ProductRecord>();

    public DbSet<Records.ProductVariantRecord> ProductVariants => Set<Records.ProductVariantRecord>();

    public DbSet<Records.AdminUserRecord> AdminUsers => Set<Records.AdminUserRecord>();

    public DbSet<Records.SessionRecord> Sessions => Set<Records.SessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 只扫描本程序集内显式声明的配置，禁止隐式发现（可预测性优先）
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
