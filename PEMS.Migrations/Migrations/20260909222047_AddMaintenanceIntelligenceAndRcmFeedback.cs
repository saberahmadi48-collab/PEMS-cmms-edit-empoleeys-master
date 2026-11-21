using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceIntelligenceAndRcmFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FailureRecords_EquipmentId",
                table: "FailureRecords");

            migrationBuilder.AlterColumn<string>(
                name: "WorkOrderType",
                table: "WorkOrders",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RcmReviewRecommendations",
                columns: table => new
                {
                    RcmReviewRecommendationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceStrategyId = table.Column<int>(type: "int", nullable: false),
                    RCMDecisionId = table.Column<int>(type: "int", nullable: true),
                    TriggerReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RepeatFailureCount = table.Column<int>(type: "int", nullable: true),
                    FailureCountInWindow = table.Column<int>(type: "int", nullable: true),
                    TotalDowntimeHoursInWindow = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    WindowDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RcmReviewRecommendations", x => x.RcmReviewRecommendationId);
                    table.ForeignKey(
                        name: "FK_RcmReviewRecommendations_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RcmReviewRecommendations_MaintenanceStrategies_MaintenanceStrategyId",
                        column: x => x.MaintenanceStrategyId,
                        principalTable: "MaintenanceStrategies",
                        principalColumn: "MaintenanceStrategyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RcmReviewRecommendations_RCMDecisions_RCMDecisionId",
                        column: x => x.RCMDecisionId,
                        principalTable: "RCMDecisions",
                        principalColumn: "RCMDecisionId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkOrderType_CreatedDate",
                table: "WorkOrders",
                columns: new[] { "WorkOrderType", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_EquipmentId_FailureModeId_FailureDate",
                table: "FailureRecords",
                columns: new[] { "EquipmentId", "FailureModeId", "FailureDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RcmReviewRecommendations_EquipmentId",
                table: "RcmReviewRecommendations",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RcmReviewRecommendations_MaintenanceStrategyId",
                table: "RcmReviewRecommendations",
                column: "MaintenanceStrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_RcmReviewRecommendations_RCMDecisionId",
                table: "RcmReviewRecommendations",
                column: "RCMDecisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RcmReviewRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_WorkOrderType_CreatedDate",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_FailureRecords_EquipmentId_FailureModeId_FailureDate",
                table: "FailureRecords");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "WorkOrders");

            migrationBuilder.AlterColumn<string>(
                name: "WorkOrderType",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_EquipmentId",
                table: "FailureRecords",
                column: "EquipmentId");
        }
    }
}
