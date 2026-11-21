using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreRequisitionTraceabilityIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreRequisitionStatusHistories_StoreRequisitionId",
                table: "StoreRequisitionStatusHistories");

            migrationBuilder.AddColumn<string>(
                name: "RequestKey",
                table: "StoreRequisitionStatusHistories",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaterialReservationId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreRequisitionLineId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionStatusHistories_StoreRequisitionId_RequestKey",
                table: "StoreRequisitionStatusHistories",
                columns: new[] { "StoreRequisitionId", "RequestKey" },
                unique: true,
                filter: "[RequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_MaterialReservationId",
                table: "InventoryTransactions",
                column: "MaterialReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_StoreRequisitionLineId",
                table: "InventoryTransactions",
                column: "StoreRequisitionLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_MaterialReservations_MaterialReservationId",
                table: "InventoryTransactions",
                column: "MaterialReservationId",
                principalTable: "MaterialReservations",
                principalColumn: "MaterialReservationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_StoreRequisitionLines_StoreRequisitionLineId",
                table: "InventoryTransactions",
                column: "StoreRequisitionLineId",
                principalTable: "StoreRequisitionLines",
                principalColumn: "StoreRequisitionLineId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_MaterialReservations_MaterialReservationId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_StoreRequisitionLines_StoreRequisitionLineId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_StoreRequisitionStatusHistories_StoreRequisitionId_RequestKey",
                table: "StoreRequisitionStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_MaterialReservationId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_StoreRequisitionLineId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "StoreRequisitionStatusHistories");

            migrationBuilder.DropColumn(
                name: "MaterialReservationId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "StoreRequisitionLineId",
                table: "InventoryTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionStatusHistories_StoreRequisitionId",
                table: "StoreRequisitionStatusHistories",
                column: "StoreRequisitionId");
        }
    }
}
