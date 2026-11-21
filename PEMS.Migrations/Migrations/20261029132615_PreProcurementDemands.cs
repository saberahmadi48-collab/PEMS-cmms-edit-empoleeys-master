using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreProcurementDemands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProcurementDemandId",
                table: "MaterialRequisitionItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcurementDemands",
                columns: table => new
                {
                    ProcurementDemandId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MaterialRequisitionId = table.Column<int>(type: "int", nullable: true),
                    RaisedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    RaisedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RaisedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DecidedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementDemands", x => x.ProcurementDemandId);
                    table.ForeignKey(
                        name: "FK_ProcurementDemands_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementDemands_MaterialRequisitions_MaterialRequisitionId",
                        column: x => x.MaterialRequisitionId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementDemands_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementDemands_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionItems_ProcurementDemandId",
                table: "MaterialRequisitionItems",
                column: "ProcurementDemandId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementDemands_MaterialItemId_WarehouseId",
                table: "ProcurementDemands",
                columns: new[] { "MaterialItemId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementDemands_MaterialRequisitionId",
                table: "ProcurementDemands",
                column: "MaterialRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementDemands_Status",
                table: "ProcurementDemands",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementDemands_UomId",
                table: "ProcurementDemands",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementDemands_WarehouseId",
                table: "ProcurementDemands",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_ProcurementDemands_LiveSource",
                table: "ProcurementDemands",
                columns: new[] { "SourceType", "SourceId" },
                unique: true,
                filter: "[Status] IN ('Open','Converted','Accepted')");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisitionItems_ProcurementDemands_ProcurementDemandId",
                table: "MaterialRequisitionItems",
                column: "ProcurementDemandId",
                principalTable: "ProcurementDemands",
                principalColumn: "ProcurementDemandId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitionItems_ProcurementDemands_ProcurementDemandId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropTable(
                name: "ProcurementDemands");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisitionItems_ProcurementDemandId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropColumn(
                name: "ProcurementDemandId",
                table: "MaterialRequisitionItems");
        }
    }
}
