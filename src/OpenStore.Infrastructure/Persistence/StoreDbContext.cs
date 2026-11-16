using Microsoft.EntityFrameworkCore;

namespace OpenStore.Infrastructure.Persistence;

/// <summary>
/// 唯一的 DbContext：8 个限界上下文的映射平铺在同一个 context 里（模块化单体，架构 §0）。
/// </summary>
/// <remarks>
/// <para>
/// 每实体一个显式 <c>IEntityTypeConfiguration</c>（见 <c>Configurations/</c>）。
/// <b>这里应用的是"本程序集内显式声明"的配置，不依赖 EF 的约定推断</b>：约定的本质是
/// "按命名/类型猜表结构"，实体变多之后它会静默改表结构，是最难查的一类 PM 事故；
/// 而 <c>ApplyConfigurationsFromAssembly</c> 是编译期固定的确定性扫描（无运行时代码生成），
/// 与约定推断是两件事——把 20+ 张表的配置逐行 <c>ApplyConfiguration(new X())</c> 手写，
/// 只会把"忘了加一行"变成一类新的故障源。
/// </para>
/// <para>
/// M0 只映射 Catalog 的 Product/ProductVariant 与 Customers 的 AdminUser/Session；
/// M1 目录批次新增 <c>Category</c> / <c>CategoryTranslation</c> / <c>ProductCategory</c> /
/// <c>ProductTranslation</c>。其余上下文的映射在后续里程碑陆续加，迁移版本号连续，
/// 禁止"一次性大迁移"（部署文档 §5 要求迁移可单步回滚）。
/// </para>
/// </remarks>
public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<Records.ProductRecord> Products => Set<Records.ProductRecord>();

    public DbSet<Records.ProductVariantRecord> ProductVariants => Set<Records.ProductVariantRecord>();

    public DbSet<Records.AdminUserRecord> AdminUsers => Set<Records.AdminUserRecord>();

    public DbSet<Records.SessionRecord> Sessions => Set<Records.SessionRecord>();

    // ---- M1 目录批次（数据模型 §3.1）------------------------------------------
    public DbSet<Records.CategoryRecord> Categories => Set<Records.CategoryRecord>();

    public DbSet<Records.CategoryTranslationRecord> CategoryTranslations => Set<Records.CategoryTranslationRecord>();

    /// <summary>商品↔分类绑定（复合主键，含"主分类"标记）。</summary>
    public DbSet<Records.ProductCategoryRecord> ProductCategories => Set<Records.ProductCategoryRecord>();

    public DbSet<Records.ProductTranslationRecord> ProductTranslations => Set<Records.ProductTranslationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 只应用本程序集内显式声明的 IEntityTypeConfiguration（编译期固定），不依赖 EF 的约定推断
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
