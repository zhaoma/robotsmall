using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_ProductCategoryIsPrimaryCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCategory_IsPrimary_Bit",
                table: "ProductCategory",
                sql: "\"IsPrimary\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCategory_IsPrimary_Bit",
                table: "ProductCategory");
        }
    }
}
