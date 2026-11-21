using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseIntakeSupplyCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SupplyCode",
                table: "WorkOrderMaterials",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Stock");

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderMaterialId",
                table: "StoreRequisitionLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionLines_WorkOrderMaterialId",
                table: "StoreRequisitionLines",
                column: "WorkOrderMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreRequisitionLines_WorkOrderMaterials_WorkOrderMaterialId",
                table: "StoreRequisitionLines",
                column: "WorkOrderMaterialId",
                principalTable: "WorkOrderMaterials",
                principalColumn: "WorkOrderMaterialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreRequisitionLines_WorkOrderMaterials_WorkOrderMaterialId",
                table: "StoreRequisitionLines");

            migrationBuilder.DropIndex(
                name: "IX_StoreRequisitionLines_WorkOrderMaterialId",
                table: "StoreRequisitionLines");

            migrationBuilder.DropColumn(
                name: "SupplyCode",
                table: "WorkOrderMaterials");

            migrationBuilder.DropColumn(
                name: "WorkOrderMaterialId",
                table: "StoreRequisitionLines");
        }
    }
}
