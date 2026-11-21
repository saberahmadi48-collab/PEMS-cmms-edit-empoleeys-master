using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsFoundationPhase2A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductionBatchId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductionDowntimeEvents",
                columns: table => new
                {
                    ProductionDowntimeEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionRunId = table.Column<int>(type: "int", nullable: false),
                    ProductionWorkCenterId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CauseCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceWorkRequestId = table.Column<int>(type: "int", nullable: true),
                    SourceWorkOrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDowntimeEvents", x => x.ProductionDowntimeEventId);
                    table.ForeignKey(
                        name: "FK_ProductionDowntimeEvents_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDowntimeEvents_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDowntimeEvents_ProductionWorkCenters_ProductionWorkCenterId",
                        column: x => x.ProductionWorkCenterId,
                        principalTable: "ProductionWorkCenters",
                        principalColumn: "ProductionWorkCenterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDowntimeEvents_WorkOrders_SourceWorkOrderId",
                        column: x => x.SourceWorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDowntimeEvents_WorkRequests_SourceWorkRequestId",
                        column: x => x.SourceWorkRequestId,
                        principalTable: "WorkRequests",
                        principalColumn: "WorkRequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionTargets",
                columns: table => new
                {
                    ProductionTargetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: true),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    ProductionWorkCenterId = table.Column<int>(type: "int", nullable: false),
                    ShiftDefinitionId = table.Column<int>(type: "int", nullable: true),
                    TargetQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionTargets", x => x.ProductionTargetId);
                    table.ForeignKey(
                        name: "FK_ProductionTargets_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTargets_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTargets_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "ProductionOrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTargets_ProductionWorkCenters_ProductionWorkCenterId",
                        column: x => x.ProductionWorkCenterId,
                        principalTable: "ProductionWorkCenters",
                        principalColumn: "ProductionWorkCenterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionTargets_ShiftDefinitions_ShiftDefinitionId",
                        column: x => x.ShiftDefinitionId,
                        principalTable: "ShiftDefinitions",
                        principalColumn: "ShiftDefinitionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ProductionBatchId",
                table: "InventoryTransactions",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_EquipmentId",
                table: "ProductionDowntimeEvents",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_ProductionRunId",
                table: "ProductionDowntimeEvents",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_ProductionWorkCenterId",
                table: "ProductionDowntimeEvents",
                column: "ProductionWorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_SourceWorkOrderId",
                table: "ProductionDowntimeEvents",
                column: "SourceWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_SourceWorkRequestId",
                table: "ProductionDowntimeEvents",
                column: "SourceWorkRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDowntimeEvents_StartTime",
                table: "ProductionDowntimeEvents",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_MaterialItemId",
                table: "ProductionTargets",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_ProductionOrderId",
                table: "ProductionTargets",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_ProductionWorkCenterId",
                table: "ProductionTargets",
                column: "ProductionWorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_ShiftDefinitionId",
                table: "ProductionTargets",
                column: "ShiftDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_TargetDate",
                table: "ProductionTargets",
                column: "TargetDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_TargetDate_ProductionWorkCenterId_MaterialItemId_ShiftDefinitionId",
                table: "ProductionTargets",
                columns: new[] { "TargetDate", "ProductionWorkCenterId", "MaterialItemId", "ShiftDefinitionId" },
                unique: true,
                filter: "[ShiftDefinitionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionTargets_UomId",
                table: "ProductionTargets",
                column: "UomId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_ProductionBatches_ProductionBatchId",
                table: "InventoryTransactions",
                column: "ProductionBatchId",
                principalTable: "ProductionBatches",
                principalColumn: "ProductionBatchId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_ProductionBatches_ProductionBatchId",
                table: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "ProductionDowntimeEvents");

            migrationBuilder.DropTable(
                name: "ProductionTargets");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_ProductionBatchId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ProductionBatchId",
                table: "InventoryTransactions");
        }
    }
}
