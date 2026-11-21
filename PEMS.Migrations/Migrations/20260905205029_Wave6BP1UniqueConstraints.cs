using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave6BP1UniqueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentCriticalities_EquipmentId",
                table: "EquipmentCriticalities");

            migrationBuilder.AlterColumn<string>(
                name: "MRNumber",
                table: "MaterialRequisitions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_MRNumber",
                table: "MaterialRequisitions",
                column: "MRNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCriticalities_EquipmentId",
                table: "EquipmentCriticalities",
                column: "EquipmentId",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisitions_MRNumber",
                table: "MaterialRequisitions");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentCriticalities_EquipmentId",
                table: "EquipmentCriticalities");

            migrationBuilder.AlterColumn<string>(
                name: "MRNumber",
                table: "MaterialRequisitions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCriticalities_EquipmentId",
                table: "EquipmentCriticalities",
                column: "EquipmentId");
        }
    }
}
