using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockItem",
                columns: table => new
                {
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    Reserved = table.Column<int>(type: "INTEGER", nullable: false),
                    Sold = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItem", x => x.VariantId);
                    table.CheckConstraint("CK_StockItem_NoOversell", "\"OnHand\" >= \"Reserved\" + \"Sold\"");
                    table.CheckConstraint("CK_StockItem_NonNegative", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Sold\" >= 0");
                    table.ForeignKey(
                        name: "FK_StockItem_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockLedger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<int>(type: "INTEGER", nullable: false),
                    OnHandDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservedDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    OnHandAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservedAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrderLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorType = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperatorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLedger_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_ChangeType_Created",
                table: "StockLedger",
                columns: new[] { "ChangeType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_CreatedAt",
                table: "StockLedger",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_Order",
                table: "StockLedger",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_Variant_Id",
                table: "StockLedger",
                columns: new[] { "VariantId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockItem");

            migrationBuilder.DropTable(
                name: "StockLedger");
        }
    }
}
