using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M1 目录批次：建 <c>Category</c> / <c>CategoryTranslation</c> / <c>ProductCategory</c> / <c>ProductTranslation</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠️ 本迁移是 `docs/02-数据模型.md` §6.6「不可逆顺序约束：先回填、后 drop」的第 ① 步，不是全部。</b>
    /// 那条约束的完整顺序是：
    /// </para>
    /// <list type="number">
    /// <item><b>①</b> 建 <c>Category</c> / <c>ProductCategory</c>（含 <c>Path</c> 物化路径）—— <b>本迁移只做这一步</b>。</item>
    /// <item><b>②</b> 为每个商品回填 <c>ProductCategory</c>，且<b>每商品恰好一行 <c>IsPrimary = true</c></b>（§3.1 那张两行表）。</item>
    /// <item><b>③</b> 校验：节点数、每节点直属/子树商品数、每商品 primary 恰好一行。</item>
    /// <item><b>④</b> 迁移 slug → <c>ProductTranslation.Slug</c>（默认语种一行），并建 <c>(Locale, Slug)</c> 唯一索引。</item>
    /// <item><b>⑤</b> 代码切换读路径（业务键从 <c>Product.Slug</c> 改为 <c>ProductTranslation.Slug</c>）。</item>
    /// <item><b>⑥</b> 以上全部通过<b>且跨 ≥1 个版本之后</b>，才允许 drop <c>Product.Slug</c>。</item>
    /// </list>
    /// <para>
    /// <b>所以：如果你是为了「把 <c>Product.Slug</c> 这个临时列删掉」而打开这个文件，现在还不能删。</b>
    /// M0 阶段 slug 同时承担了<b>分类归属的唯一表达</b>（基准数据用 slug 的 <c>bench-cNN-</c> 段编码分类，
    /// 没有第二个来源）；②③ 出错还能重算，<b><c>Product.Slug</c> 一旦 drop 就没有输入了</b>——
    /// 这是这条链上唯一不可逆的一步。
    /// </para>
    /// <para>
    /// <b>判据（drop 任何列之前先问一句）</b>：这一列是不是某件还没完成的事的唯一输入？
    /// 是，就必须排在那个动作之后，并且在它被 drop 之前留一份可复核的导出（这里就是回填报告：
    /// 每分类直属数 / 子树数 / 缺 primary 的商品列表）。
    /// </para>
    /// <para>
    /// 反向兼容说明：<c>Down()</c> 只删本迁移新建的 4 张表，<b>不触碰 <c>Product</c>/<c>ProductVariant</c></b>
    /// —— 因此本迁移的 Up/Down 都不会让 M0 的数据形状发生任何变化。
    /// </para>
    /// </remarks>
    public partial class M1_Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    AttributeSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.CheckConstraint("CK_Category_Depth", "\"Depth\" >= 0 AND \"Depth\" <= 5");
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ShortDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductTranslation_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTranslation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTranslation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryTranslation_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategory", x => new { x.ProductId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_ProductCategory_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCategory_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Category_Parent_Sort",
                table: "Category",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Category_Path",
                table: "Category",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "UX_CategoryTranslation_Category_Locale",
                table: "CategoryTranslation",
                columns: new[] { "CategoryId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CategoryTranslation_Locale_Slug",
                table: "CategoryTranslation",
                columns: new[] { "Locale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategory_Category_Product",
                table: "ProductCategory",
                columns: new[] { "CategoryId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategory_PrimaryPerProduct",
                table: "ProductCategory",
                column: "ProductId",
                unique: true,
                filter: "\"IsPrimary\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_ProductTranslation_Locale_Slug",
                table: "ProductTranslation",
                columns: new[] { "Locale", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductTranslation_Product_Locale",
                table: "ProductTranslation",
                columns: new[] { "ProductId", "Locale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryTranslation");

            migrationBuilder.DropTable(
                name: "ProductCategory");

            migrationBuilder.DropTable(
                name: "ProductTranslation");

            migrationBuilder.DropTable(
                name: "Category");
        }
    }
}
