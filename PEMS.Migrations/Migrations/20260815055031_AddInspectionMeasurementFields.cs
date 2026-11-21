using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionMeasurementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LowerLimit",
                table: "InspectionRecords",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementUnit",
                table: "InspectionRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeasurementValue",
                table: "InspectionRecords",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UpperLimit",
                table: "InspectionRecords",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LowerLimit",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "MeasurementUnit",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "MeasurementValue",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "UpperLimit",
                table: "InspectionRecords");
        }
    }
}
