using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_ConditionReadingInstrumentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InstrumentId",
                table: "ConditionReadings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConditionReadings_InstrumentId",
                table: "ConditionReadings",
                column: "InstrumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConditionReadings_Instruments_InstrumentId",
                table: "ConditionReadings",
                column: "InstrumentId",
                principalTable: "Instruments",
                principalColumn: "InstrumentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConditionReadings_Instruments_InstrumentId",
                table: "ConditionReadings");

            migrationBuilder.DropIndex(
                name: "IX_ConditionReadings_InstrumentId",
                table: "ConditionReadings");

            migrationBuilder.DropColumn(
                name: "InstrumentId",
                table: "ConditionReadings");
        }
    }
}
