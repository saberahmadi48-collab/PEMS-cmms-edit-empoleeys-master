using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstrumentPassportAndCalibrationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Accuracy",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlarmSetpoint",
                table: "Instruments",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarrierRelay",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CableNumber",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Communication",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentStatus",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ElectricalConnection",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExProtection",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailAction",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GasGroup",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HazardousArea",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IOCardChannel",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IPRating",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JunctionBox",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCalibrationDate",
                table: "Instruments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMaintenanceDate",
                table: "Instruments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoopNumber",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarshallingCabinet",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextCalibrationDue",
                table: "Instruments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMaintenanceDue",
                table: "Instruments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputSignal",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PowerSupply",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessConnection",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessVariable",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SIL",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignalClass",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "System",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemCabinet",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempClass",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TripSetpoint",
                table: "Instruments",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Zone",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalibrationHistories",
                columns: table => new
                {
                    CalibrationHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    CalibrationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalibrationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StandardReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RangeUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AsFound = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AsLeft = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TechnicianEmployeeId = table.Column<int>(type: "int", nullable: true),
                    CertificateNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalibrationHistories", x => x.CalibrationHistoryId);
                    table.ForeignKey(
                        name: "FK_CalibrationHistories_Employees_TechnicianEmployeeId",
                        column: x => x.TechnicianEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalibrationHistories_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationHistories_InstrumentId",
                table: "CalibrationHistories",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationHistories_TechnicianEmployeeId",
                table: "CalibrationHistories",
                column: "TechnicianEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalibrationHistories");

            migrationBuilder.DropColumn(
                name: "Accuracy",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "AlarmSetpoint",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "BarrierRelay",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "CableNumber",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Communication",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "CurrentStatus",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "ElectricalConnection",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "ExProtection",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "FailAction",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "GasGroup",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "HazardousArea",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IOCardChannel",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IPRating",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "JunctionBox",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "LastCalibrationDate",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "LastMaintenanceDate",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "LoopNumber",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "MarshallingCabinet",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "NextCalibrationDue",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "NextMaintenanceDue",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "OutputSignal",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "PowerSupply",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "ProcessConnection",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "ProcessVariable",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "SIL",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "SignalClass",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "System",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "SystemCabinet",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "TempClass",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "TripSetpoint",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Zone",
                table: "Instruments");
        }
    }
}
