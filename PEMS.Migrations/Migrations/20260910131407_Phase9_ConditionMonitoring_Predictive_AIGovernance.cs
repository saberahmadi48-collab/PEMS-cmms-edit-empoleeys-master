using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_ConditionMonitoring_Predictive_AIGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIReliabilityInsights",
                columns: table => new
                {
                    AIReliabilityInsightId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: true),
                    InsightType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceDataSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIReliabilityInsights", x => x.AIReliabilityInsightId);
                    table.ForeignKey(
                        name: "FK_AIReliabilityInsights_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIReliabilityInsights_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIReliabilityInsights_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConditionMonitoringPoints",
                columns: table => new
                {
                    ConditionMonitoringPointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    VariableType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AlarmThresholdLow = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AlarmThresholdHigh = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionMonitoringPoints", x => x.ConditionMonitoringPointId);
                    table.ForeignKey(
                        name: "FK_ConditionMonitoringPoints_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PredictiveMaintenanceModels",
                columns: table => new
                {
                    PredictiveMaintenanceModelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TrainingDataStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictiveMaintenanceModels", x => x.PredictiveMaintenanceModelId);
                });

            migrationBuilder.CreateTable(
                name: "ConditionReadings",
                columns: table => new
                {
                    ConditionReadingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConditionMonitoringPointId = table.Column<int>(type: "int", nullable: false),
                    ReadingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provenance = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAlarm = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionReadings", x => x.ConditionReadingId);
                    table.ForeignKey(
                        name: "FK_ConditionReadings_ConditionMonitoringPoints_ConditionMonitoringPointId",
                        column: x => x.ConditionMonitoringPointId,
                        principalTable: "ConditionMonitoringPoints",
                        principalColumn: "ConditionMonitoringPointId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PredictiveMaintenanceResults",
                columns: table => new
                {
                    PredictiveMaintenanceResultId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PredictiveMaintenanceModelId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    FailureProbability = table.Column<double>(type: "float", nullable: true),
                    RemainingUsefulLifeDays = table.Column<double>(type: "float", nullable: true),
                    AnomalyScore = table.Column<double>(type: "float", nullable: true),
                    Provenance = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictiveMaintenanceResults", x => x.PredictiveMaintenanceResultId);
                    table.ForeignKey(
                        name: "FK_PredictiveMaintenanceResults_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PredictiveMaintenanceResults_PredictiveMaintenanceModels_PredictiveMaintenanceModelId",
                        column: x => x.PredictiveMaintenanceModelId,
                        principalTable: "PredictiveMaintenanceModels",
                        principalColumn: "PredictiveMaintenanceModelId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIReliabilityInsights_EquipmentClassId",
                table: "AIReliabilityInsights",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReliabilityInsights_EquipmentId",
                table: "AIReliabilityInsights",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReliabilityInsights_ReviewedById",
                table: "AIReliabilityInsights",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AIReliabilityInsights_ReviewStatus",
                table: "AIReliabilityInsights",
                column: "ReviewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionMonitoringPoints_EquipmentId",
                table: "ConditionMonitoringPoints",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionReadings_PointId_ReadingDate",
                table: "ConditionReadings",
                columns: new[] { "ConditionMonitoringPointId", "ReadingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PredictiveMaintenanceResults_EquipmentId_GeneratedDate",
                table: "PredictiveMaintenanceResults",
                columns: new[] { "EquipmentId", "GeneratedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PredictiveMaintenanceResults_PredictiveMaintenanceModelId",
                table: "PredictiveMaintenanceResults",
                column: "PredictiveMaintenanceModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIReliabilityInsights");

            migrationBuilder.DropTable(
                name: "ConditionReadings");

            migrationBuilder.DropTable(
                name: "PredictiveMaintenanceResults");

            migrationBuilder.DropTable(
                name: "ConditionMonitoringPoints");

            migrationBuilder.DropTable(
                name: "PredictiveMaintenanceModels");
        }
    }
}
