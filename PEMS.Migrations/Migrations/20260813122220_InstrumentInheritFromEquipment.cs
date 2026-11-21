using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentInheritFromEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instruments_EquipmentId",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "TagNumber",
                table: "Instruments");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_EquipmentId",
                table: "Instruments",
                column: "EquipmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instruments_EquipmentId",
                table: "Instruments");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagNumber",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_EquipmentId",
                table: "Instruments",
                column: "EquipmentId");
        }
    }
}
