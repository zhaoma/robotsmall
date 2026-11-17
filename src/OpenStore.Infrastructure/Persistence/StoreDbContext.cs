using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenStore.Infrastructure.Persistence.Converters;

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
/// <b>关于"迁移版本号连续、禁止一次性大迁移"这句旧注释已被取代</b>：
/// 那是 M0 期按「有存量数据的环境」写的。M1 的裁决（<c>docs/02 §3.1</c>）明确
/// 「模型定稿时 squash 成一个 <c>M1CatalogInventoryAuth</c> 装完 24 张表」，
/// 理由是当时 dev 库无存量数据、无外部消费者，而增量加 CHECK/改列在 SQLite 上
/// 只能靠重建表实现 —— 那才是真正的风险源。**M1 之后**恢复纯增量，
/// 那时"可单步回滚"才是有效约束。
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

    // ---- M1 属性体系（数据模型 §3.1 / 对照表 §1.2.7~§1.2.12）------------------
    public DbSet<Records.AttributeGroupRecord> AttributeGroups => Set<Records.AttributeGroupRecord>();

    public DbSet<Records.AttributeSetRecord> AttributeSets => Set<Records.AttributeSetRecord>();

    public DbSet<Records.AttributeDefinitionRecord> AttributeDefinitions => Set<Records.AttributeDefinitionRecord>();

    public DbSet<Records.AttributeDefinitionTranslationRecord> AttributeDefinitionTranslations => Set<Records.AttributeDefinitionTranslationRecord>();

    public DbSet<Records.AttributeOptionRecord> AttributeOptions => Set<Records.AttributeOptionRecord>();

    public DbSet<Records.AttributeOptionTranslationRecord> AttributeOptionTranslations => Set<Records.AttributeOptionTranslationRecord>();

    /// <summary>集合成员关系（复合主键，无独立 Id）。</summary>
    public DbSet<Records.AttributeSetMemberRecord> AttributeSetMembers => Set<Records.AttributeSetMemberRecord>();

    /// <summary>分类对继承属性的覆盖（复合主键，无独立 Id）。</summary>
    public DbSet<Records.CategoryAttributeOverrideRecord> CategoryAttributeOverrides => Set<Records.CategoryAttributeOverrideRecord>();

    /// <summary>商品级属性值（窄表，四列恰好一列）。</summary>
    public DbSet<Records.ProductAttributeValueRecord> ProductAttributeValues => Set<Records.ProductAttributeValueRecord>();

    /// <summary>变体级属性值（每轴恰好一行）。</summary>
    public DbSet<Records.VariantAttributeValueRecord> VariantAttributeValues => Set<Records.VariantAttributeValueRecord>();

    /// <summary>商品的变体轴定义（轴顺序 1..3）。</summary>
    public DbSet<Records.ProductVariantAxisRecord> ProductVariantAxes => Set<Records.ProductVariantAxisRecord>();

    // ---- M1 媒体（数据模型 §3.1 / 对照表 §1.2.14）-----------------------------
    /// <summary>媒体资产（不可变表）。</summary>
    public DbSet<Records.MediaAssetRecord> MediaAssets => Set<Records.MediaAssetRecord>();

    /// <summary>商品/变体 ↔ 媒体资产绑定。</summary>
    public DbSet<Records.ProductImageRecord> ProductImages => Set<Records.ProductImageRecord>();

    // ---- M1 库存批次（数据模型 §3.2）------------------------------------------
    public DbSet<Records.StockItemRecord> StockItems => Set<Records.StockItemRecord>();

    /// <summary>库存流水：<b>只追加</b>，仓储不得暴露 Update / Delete。</summary>
    public DbSet<Records.StockLedgerRecord> StockLedger => Set<Records.StockLedgerRecord>();

    // ---- 基础设施（数据模型 §1.5）---------------------------------------------
    /// <summary>审计流水：<b>只追加、不可变</b>，仓储不得暴露 Update / Delete。</summary>
    public DbSet<Records.AuditLogRecord> AuditLogs => Set<Records.AuditLogRecord>();

    /// <summary>
    /// 全局类型约定：<b>用一处声明覆盖所有表，而不是每列写一遍</b>（C11 的"3 个全局基础设施"之二）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么 <c>DateTime</c> 需要转换器</b>：SQLite 是 <c>TEXT</c>，读回来的
    /// <c>Kind = Unspecified</c>，序列化成 JSON 后前端会按浏览器本地时区解析，
    /// 同一行数据在不同时区显示成两个时刻且不报错。转换器只补 <c>Kind</c>，不改数值
    /// （理由见 <see cref="DateTimeUtcConverter"/>：换算会引入"服务器时区"这个隐藏输入）。
    /// </para>
    /// <para>
    /// <b>为什么枚举统一 <c>HasConversion&lt;int&gt;()</c></b>：默认 EF 对枚举就存整数，
    /// 但显式声明能防止某天有人给单个属性加 <c>HasConversion&lt;string&gt;()</c> 造成
    /// "一半表存整数、一半存字符串"——那会让跨表 join 与索引失效，且不会有任何报错。
    /// 项目内已有 <c>ProductStatus</c>/<c>CategoryStatus</c> 等枚举列按整数落库，
    /// 这条约定让"新加枚举"自动继承同一口径。
    /// </para>
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<DateTimeUtcConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableDateTimeUtcConverter>();

        configurationBuilder.Properties<Enum>()
            .HaveConversion<int>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 只应用本程序集内显式声明的 IEntityTypeConfiguration（编译期固定），不依赖 EF 的约定推断
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
