using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_MaintenanceStrategy_CBM_ConditionMonitoringLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConditionMonitoringPointId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_ConditionMonitoringPointId",
                table: "MaintenanceStrategies",
                column: "ConditionMonitoringPointId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_ConditionMonitoringPoints_ConditionMonitoringPointId",
                table: "MaintenanceStrategies",
                column: "ConditionMonitoringPointId",
                principalTable: "ConditionMonitoringPoints",
                principalColumn: "ConditionMonitoringPointId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_ConditionMonitoringPoints_ConditionMonitoringPointId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_ConditionMonitoringPointId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ConditionMonitoringPointId",
                table: "MaintenanceStrategies");
        }
    }
}
