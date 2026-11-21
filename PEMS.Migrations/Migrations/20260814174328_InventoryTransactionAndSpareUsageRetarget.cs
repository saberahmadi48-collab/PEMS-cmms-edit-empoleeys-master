using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryTransactionAndSpareUsageRetarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceSpareUsages_SpareParts_SparePartId",
                table: "MaintenanceSpareUsages");

            migrationBuilder.RenameColumn(
                name: "SparePartId",
                table: "MaintenanceSpareUsages",
                newName: "MaterialItemId");

            migrationBuilder.RenameIndex(
                name: "IX_MaintenanceSpareUsages_SparePartId",
                table: "MaintenanceSpareUsages",
                newName: "IX_MaintenanceSpareUsages_MaterialItemId");

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    StorageLocationId = table.Column<int>(type: "int", nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.InventoryTransactionId);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "StorageLocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_EmployeeId",
                table: "InventoryTransactions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_MaterialItemId_WarehouseId_TransactionDate",
                table: "InventoryTransactions",
                columns: new[] { "MaterialItemId", "WarehouseId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StorageLocationId",
                table: "InventoryTransactions",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_UomId",
                table: "InventoryTransactions",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WarehouseId",
                table: "InventoryTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_WorkOrderId",
                table: "InventoryTransactions",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceSpareUsages_MaterialItems_MaterialItemId",
                table: "MaintenanceSpareUsages",
                column: "MaterialItemId",
                principalTable: "MaterialItems",
                principalColumn: "MaterialItemId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceSpareUsages_MaterialItems_MaterialItemId",
                table: "MaintenanceSpareUsages");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");

            migrationBuilder.RenameColumn(
                name: "MaterialItemId",
                table: "MaintenanceSpareUsages",
                newName: "SparePartId");

            migrationBuilder.RenameIndex(
                name: "IX_MaintenanceSpareUsages_MaterialItemId",
                table: "MaintenanceSpareUsages",
                newName: "IX_MaintenanceSpareUsages_SparePartId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceSpareUsages_SpareParts_SparePartId",
                table: "MaintenanceSpareUsages",
                column: "SparePartId",
                principalTable: "SpareParts",
                principalColumn: "SparePartId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
