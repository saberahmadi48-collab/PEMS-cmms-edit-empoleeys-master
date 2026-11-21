using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_MechanicalEquipmentPassport_UniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaticEquipments_MechanicalEquipmentId",
                table: "StaticEquipments");

            migrationBuilder.DropIndex(
                name: "IX_RotatingEquipments_MechanicalEquipmentId",
                table: "RotatingEquipments");

            migrationBuilder.DropIndex(
                name: "IX_MechanicalEquipments_EquipmentId",
                table: "MechanicalEquipments");

            migrationBuilder.CreateIndex(
                name: "IX_StaticEquipments_MechanicalEquipmentId",
                table: "StaticEquipments",
                column: "MechanicalEquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotatingEquipments_MechanicalEquipmentId",
                table: "RotatingEquipments",
                column: "MechanicalEquipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MechanicalEquipments_EquipmentId",
                table: "MechanicalEquipments",
                column: "EquipmentId",
                unique: true,
                filter: "[EquipmentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaticEquipments_MechanicalEquipmentId",
                table: "StaticEquipments");

            migrationBuilder.DropIndex(
                name: "IX_RotatingEquipments_MechanicalEquipmentId",
                table: "RotatingEquipments");

            migrationBuilder.DropIndex(
                name: "IX_MechanicalEquipments_EquipmentId",
                table: "MechanicalEquipments");

            migrationBuilder.CreateIndex(
                name: "IX_StaticEquipments_MechanicalEquipmentId",
                table: "StaticEquipments",
                column: "MechanicalEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RotatingEquipments_MechanicalEquipmentId",
                table: "RotatingEquipments",
                column: "MechanicalEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MechanicalEquipments_EquipmentId",
                table: "MechanicalEquipments",
                column: "EquipmentId");
        }
    }
}
