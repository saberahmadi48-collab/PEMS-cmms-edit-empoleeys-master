using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_MechanicalPassport_CM_EvaluationProfile_Finding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceIdentifier",
                table: "WorkRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "WorkRequests",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Edition",
                table: "StandardReferences",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFrom",
                table: "StandardReferences",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveTo",
                table: "StandardReferences",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedDate",
                table: "StandardReferences",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "StandardReferences",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Current");

            migrationBuilder.AddColumn<int>(
                name: "EvaluationProfileId",
                table: "ConditionReadings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvaluationProfileVersion",
                table: "ConditionReadings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationStatus",
                table: "ConditionReadings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotEvaluated");

            migrationBuilder.AddColumn<int>(
                name: "EvaluationProfileId",
                table: "ConditionMonitoringPoints",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvaluationProfiles",
                columns: table => new
                {
                    EvaluationProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StandardReferenceId = table.Column<int>(type: "int", nullable: true),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: true),
                    VariableType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OperatingCondition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationProfiles", x => x.EvaluationProfileId);
                    table.ForeignKey(
                        name: "FK_EvaluationProfiles_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationProfiles_StandardReferences_StandardReferenceId",
                        column: x => x.StandardReferenceId,
                        principalTable: "StandardReferences",
                        principalColumn: "StandardReferenceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    FindingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    ConditionReadingId = table.Column<int>(type: "int", nullable: true),
                    InspectionRecordId = table.Column<int>(type: "int", nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecommendedAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureModeId = table.Column<int>(type: "int", nullable: true),
                    FailureCauseId = table.Column<int>(type: "int", nullable: true),
                    WorkRequestId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.FindingId);
                    table.ForeignKey(
                        name: "FK_Findings_ConditionReadings_ConditionReadingId",
                        column: x => x.ConditionReadingId,
                        principalTable: "ConditionReadings",
                        principalColumn: "ConditionReadingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Findings_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Findings_FailureCauses_FailureCauseId",
                        column: x => x.FailureCauseId,
                        principalTable: "FailureCauses",
                        principalColumn: "FailureCauseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Findings_FailureModes_FailureModeId",
                        column: x => x.FailureModeId,
                        principalTable: "FailureModes",
                        principalColumn: "FailureModeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Findings_InspectionRecords_InspectionRecordId",
                        column: x => x.InspectionRecordId,
                        principalTable: "InspectionRecords",
                        principalColumn: "InspectionRecordId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Findings_WorkRequests_WorkRequestId",
                        column: x => x.WorkRequestId,
                        principalTable: "WorkRequests",
                        principalColumn: "WorkRequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationLimits",
                columns: table => new
                {
                    EvaluationLimitId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationProfileId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LowValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HighValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LowInclusive = table.Column<bool>(type: "bit", nullable: false),
                    HighInclusive = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationLimits", x => x.EvaluationLimitId);
                    table.ForeignKey(
                        name: "FK_EvaluationLimits_EvaluationProfiles_EvaluationProfileId",
                        column: x => x.EvaluationProfileId,
                        principalTable: "EvaluationProfiles",
                        principalColumn: "EvaluationProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionMonitoringPoints_EvaluationProfileId",
                table: "ConditionMonitoringPoints",
                column: "EvaluationProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationLimits_EvaluationProfileId_Status",
                table: "EvaluationLimits",
                columns: new[] { "EvaluationProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationProfiles_Code",
                table: "EvaluationProfiles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationProfiles_EquipmentClassId",
                table: "EvaluationProfiles",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationProfiles_StandardReferenceId",
                table: "EvaluationProfiles",
                column: "StandardReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationProfiles_VariableType_EquipmentClassId_IsActive",
                table: "EvaluationProfiles",
                columns: new[] { "VariableType", "EquipmentClassId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ConditionReadingId",
                table: "Findings",
                column: "ConditionReadingId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_EquipmentId",
                table: "Findings",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_FailureCauseId",
                table: "Findings",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_FailureModeId",
                table: "Findings",
                column: "FailureModeId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_InspectionRecordId",
                table: "Findings",
                column: "InspectionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_Status",
                table: "Findings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_WorkRequestId",
                table: "Findings",
                column: "WorkRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConditionMonitoringPoints_EvaluationProfiles_EvaluationProfileId",
                table: "ConditionMonitoringPoints",
                column: "EvaluationProfileId",
                principalTable: "EvaluationProfiles",
                principalColumn: "EvaluationProfileId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConditionMonitoringPoints_EvaluationProfiles_EvaluationProfileId",
                table: "ConditionMonitoringPoints");

            migrationBuilder.DropTable(
                name: "EvaluationLimits");

            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "EvaluationProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ConditionMonitoringPoints_EvaluationProfileId",
                table: "ConditionMonitoringPoints");

            migrationBuilder.DropColumn(
                name: "SourceIdentifier",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "Edition",
                table: "StandardReferences");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "StandardReferences");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "StandardReferences");

            migrationBuilder.DropColumn(
                name: "PublishedDate",
                table: "StandardReferences");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "StandardReferences");

            migrationBuilder.DropColumn(
                name: "EvaluationProfileId",
                table: "ConditionReadings");

            migrationBuilder.DropColumn(
                name: "EvaluationProfileVersion",
                table: "ConditionReadings");

            migrationBuilder.DropColumn(
                name: "EvaluationStatus",
                table: "ConditionReadings");

            migrationBuilder.DropColumn(
                name: "EvaluationProfileId",
                table: "ConditionMonitoringPoints");
        }
    }
}
