using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoLineReceivingAndIncomingSupply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InspectionReleasedAt",
                table: "PurchaseOrderLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionReleasedByUserName",
                table: "PurchaseOrderLines",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionTestPlanId",
                table: "PurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "PurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HoldAcceptedQuantity",
                table: "IrnReceiptLines",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HoldRejectedQuantity",
                table: "IrnReceiptLines",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldResolutionNotes",
                table: "IrnReceiptLines",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldResolvedAt",
                table: "IrnReceiptLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldResolvedByUserName",
                table: "IrnReceiptLines",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineId",
                table: "IrnReceiptLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionAction",
                table: "IrnReceiptLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectionDecidedAt",
                table: "IrnReceiptLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionDecidedByUserName",
                table: "IrnReceiptLines",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_InspectionTestPlanId",
                table: "PurchaseOrderLines",
                column: "InspectionTestPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_WarehouseId",
                table: "PurchaseOrderLines",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptLines_PurchaseOrderLineId",
                table: "IrnReceiptLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_IrnReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "IrnReceiptLines",
                column: "PurchaseOrderLineId",
                principalTable: "PurchaseOrderLines",
                principalColumn: "PurchaseOrderLineId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_InspectionTestPlans_InspectionTestPlanId",
                table: "PurchaseOrderLines",
                column: "InspectionTestPlanId",
                principalTable: "InspectionTestPlans",
                principalColumn: "InspectionTestPlanId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_Warehouses_WarehouseId",
                table: "PurchaseOrderLines",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IrnReceiptLines_PurchaseOrderLines_PurchaseOrderLineId",
                table: "IrnReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_InspectionTestPlans_InspectionTestPlanId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_Warehouses_WarehouseId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_InspectionTestPlanId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_WarehouseId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_IrnReceiptLines_PurchaseOrderLineId",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "InspectionReleasedAt",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "InspectionReleasedByUserName",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "InspectionTestPlanId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "HoldAcceptedQuantity",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "HoldRejectedQuantity",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "HoldResolutionNotes",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "HoldResolvedAt",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "HoldResolvedByUserName",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "RejectionAction",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "RejectionDecidedAt",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "RejectionDecidedByUserName",
                table: "IrnReceiptLines");
        }
    }
}
