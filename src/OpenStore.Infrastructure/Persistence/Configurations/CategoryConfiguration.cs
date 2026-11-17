using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenStore.Infrastructure.Persistence.Records;

namespace OpenStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>Category</c> 表映射（显式，无约定推断）。
/// </summary>
/// <remarks>
/// <para>
/// <b><c>CK_Category_Depth</c> 用表级 API 声明在这里，不许写成 <c>migrationBuilder.Sql</c></b>
/// （数据模型 §4.1.1 的硬规约 R-CHECK）：EF 的 SQLite 迁移在遇到 <c>DropColumn</c>/<c>AlterColumn</c>
/// 时会<b>重建表</b>，重建用的是"模型里声明的 schema"——手写 SQL 的 CHECK 不在模型里，
/// 下一次重建后约束会<b>凭空消失且无任何报错</b>。
/// </para>
/// <para>
/// <b><c>CK_Category_Depth</c> 的下界是 <c>1</c> 不是 <c>0</c></b>（数据模型 §3.1）：<c>Path</c> 的段是 id
/// （<c>/1/7/23/</c>），所以唯一自洽的定义是 <b><c>Depth = Path 的段数</c>（含自身）</b>，根节点 = 1 段。
/// 写成 <c>&gt;= 0</c> 时 <c>Depth = 0</c> 与它自己的 <c>Path</c> 自相矛盾，而上界 5 也从「共 5 层」
/// 变成「共 6 层」——<b>这两处都不报错</b>，只让「层级超限」的告警晚一层触发。
/// 原始 <c>M1_Catalog</c> 建表时写的是 <c>&gt;= 0</c>，由 <c>M1_CategoryDepthLowerBound</c> 迁移修正
/// （名字未变、只有表达式变 → 一条 <c>DropCheckConstraint</c> + 一条 <c>AddCheckConstraint</c>）。
/// </para>
/// <para>
/// <b><c>Depth</c> 刻意没有 <c>HasDefaultValue</c></b>：它必须等于 <c>Path</c> 的段数，任何 DB 侧默认值
/// 都只会让「忘记赋值」静默变成「赋了一个错的值」并当场通过 <c>CHECK</c>。全表同理没有 DB 默认值，
/// 对照表「默认」列记的是<b>应用侧应赋的值</b>，不是 DDL 里的 <c>DEFAULT</c>。
/// </para>
/// <para>
/// <b><c>IX_Category_Path</c> 不是可选优化</b>（数据模型 §4.1）：分类页要含子树全部商品，
/// 省掉前缀索引就要为每个后代分类各查一次 <c>ProductCategory</c>，而越靠近根的节点展开的 id 越多。
/// </para>
/// <para>
/// <b><c>AttributeSetId</c> 的外键在本批落地</b>（docs §1.2.5 定的是「FK → <c>AttributeSet</c> <c>Restrict</c>」）：
/// 早期注释写「本轮无外键，因为 <c>AttributeSet</c> 属属性批次」—— 那句在
/// <c>AttributeSet</c> 与 <c>Category</c> 同一批进 <c>M1CatalogInventoryAuth</c> 之后已不成立。
/// <c>Restrict</c>：还有分类引用某个属性集时，该属性集不得物理删除。
/// FK 索引由 EF 自动补 <c>IX_Category_AttributeSetId</c>，不手工建（手工建会与自动索引撞名）。
/// </para>
/// </remarks>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<CategoryRecord>
{
    /// <summary>深度上限的 DB 侧表达；与 <see cref="Domain.Catalog.Category.MaxDepth"/> 同口径。</summary>
    public const string DepthCheckName = "CK_Category_Depth";

    public void Configure(EntityTypeBuilder<CategoryRecord> builder)
    {
        builder.ToTable("Category", t => t.HasCheckConstraint(DepthCheckName, "\"Depth\" >= 1 AND \"Depth\" <= 5"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Path)
            .HasMaxLength(400)
            .IsRequired();

        builder.Property(c => c.Depth).IsRequired();
        builder.Property(c => c.SortOrder).IsRequired();
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.IsDeleted).IsRequired();

        // Version：并发令牌（数据模型 §1.4 把 Category 列进"需要乐观锁"的第一梯队）。
        // 自增由 AuditFieldsInterceptor 统一维护（Added 置 1 / Modified 自增），
        // **不允许映射层手工赋值**。之前这条注释写"拦截器尚未接入、属 M2"，已随 C11 落地作废。
        builder.Property(c => c.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // 自引用：删父级不得连带删子树（Restrict）。用无导航属性的写法，
        // 因为领域侧通过 Id 引用（聚合边界），导航属性只会在序列化时制造循环。
        builder.HasOne<CategoryRecord>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 导航树组装：一次全表读 ORDER BY ParentId, SortOrder（分类总量百级，整体进内存缓存）
        builder.HasIndex(c => new { c.ParentId, c.SortOrder })
            .HasDatabaseName("IX_Category_Parent_Sort");

        // 分类页主路径：Path LIKE '/xxx/%' → 子树 id 集
        builder.HasIndex(c => c.Path)
            .HasDatabaseName("IX_Category_Path");

        // 生效属性集 → AttributeSet，Restrict：还有分类引用时不得物理删除该集合
        builder.HasOne<AttributeSetRecord>()
            .WithMany()
            .HasForeignKey(c => c.AttributeSetId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
