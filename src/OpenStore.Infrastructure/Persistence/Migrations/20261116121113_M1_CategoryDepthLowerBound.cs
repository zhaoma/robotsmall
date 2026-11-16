using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 把 <c>CK_Category_Depth</c> 的下界从 <c>0</c> 修到 <c>1</c>（依据：数据模型 §3.1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>名字没变、只改表达式，因此是一条 Drop + 一条 Add，不是重命名。</b>约束的"身份"是名字，
    /// 表达式是它的内容——换名字会让 §4.1 的门禁按名对账时同时看见"少一条"和"多一条"两个假信号。
    /// </para>
    /// <para>
    /// <b>为什么下界是 1：</b><c>Path</c> 的段是 id（<c>/1/7/23/</c>），唯一自洽的定义是
    /// <c>Depth = Path 的段数</c>（含自身），根节点 = 1 段。写成 <c>&gt;= 0</c> 时 <c>Depth = 0</c>
    /// 与它自己的 <c>Path</c> 自相矛盾，而上界 <c>5</c> 也从"共 5 层"变成"共 6 层"——
    /// <b>这两处都不报错</b>，只让"层级超限"的告警晚一层触发。
    /// </para>
    /// <para>
    /// <b>SQLite 侧会重建 <c>Category</c> 表</b>（<c>ef_temp_Category</c> → 拷数据 → drop → rename），
    /// 因为 SQLite 不支持 <c>ALTER TABLE DROP CONSTRAINT</c>。约束仍然声明在
    /// <c>CategoryConfiguration</c> 里（R-CHECK 规约），所以重建时它还在模型里、不会静默丢。
    /// <c>Down</c> 会把下界退回 <c>0</c> 并<b>再重建一次</b>——回滚比前进贵一次重建，这是有意的：
    /// 回滚后库回到"文档未修"的宽松状态，而不是"半修"。
    /// </para>
    /// <para>
    /// 没有用 <c>M1_Catalog</c> 原地改表达式，因为那一笔已在 main 上被实测认可（<c>ad65e6b</c>）；
    /// 改写已发布的迁移会让同一份迁移文件在不同分支产生不同 DDL。
    /// </para>
    /// </remarks>
    public partial class M1_CategoryDepthLowerBound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Category_Depth",
                table: "Category");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Category_Depth",
                table: "Category",
                sql: "\"Depth\" >= 1 AND \"Depth\" <= 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Category_Depth",
                table: "Category");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Category_Depth",
                table: "Category",
                sql: "\"Depth\" >= 0 AND \"Depth\" <= 5");
        }
    }
}
