using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaterialStockNoLocationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 2 / P4 pre-flight (read-only guard, never merges/deletes data): if duplicate NULL-location stock rows already exist
            // the unique index cannot be created. Fail the whole migration (EF runs it in a transaction) with an actionable message instead
            // of silently picking a row. Diagnostic query and reconciliation guidance: docs/procurement/INVENTORY_STOCK_ROW_INTEGRITY.md
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [MaterialStocks] WHERE [StorageLocationId] IS NULL GROUP BY [MaterialItemId], [WarehouseId] HAVING COUNT(*) > 1)
    THROW 51000, N'P4 pre-flight failed: duplicate MaterialStocks rows (same MaterialItemId and WarehouseId with no StorageLocationId) exist. Nothing was changed. Reconcile them manually first (see docs/procurement/INVENTORY_STOCK_ROW_INTEGRITY.md), then re-run the migration.', 1;");

            migrationBuilder.CreateIndex(
                name: "UX_MaterialStocks_Item_Warehouse_NoLocation",
                table: "MaterialStocks",
                columns: new[] { "MaterialItemId", "WarehouseId" },
                unique: true,
                filter: "[StorageLocationId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_MaterialStocks_Item_Warehouse_NoLocation",
                table: "MaterialStocks");
        }
    }
}
