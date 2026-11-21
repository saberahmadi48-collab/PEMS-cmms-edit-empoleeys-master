using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRcmFmeaDecisionIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FMEAAnalyses",
                columns: table => new
                {
                    FMEAAnalysisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AnalysisDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByAnalysisId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FMEAAnalyses", x => x.FMEAAnalysisId);
                    table.ForeignKey(
                        name: "FK_FMEAAnalyses_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAAnalyses_FMEAAnalyses_SupersededByAnalysisId",
                        column: x => x.SupersededByAnalysisId,
                        principalTable: "FMEAAnalyses",
                        principalColumn: "FMEAAnalysisId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FunctionalRequirements",
                columns: table => new
                {
                    FunctionalRequirementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredFunction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformanceRequirement = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MinimumPerformance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MaximumPerformance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalRequirements", x => x.FunctionalRequirementId);
                    table.ForeignKey(
                        name: "FK_FunctionalRequirements_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RCMAnalyses",
                columns: table => new
                {
                    RCMAnalysisId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: false),
                    FMEAAnalysisId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RCMAnalyses", x => x.RCMAnalysisId);
                    table.ForeignKey(
                        name: "FK_RCMAnalyses_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RCMAnalyses_FMEAAnalyses_FMEAAnalysisId",
                        column: x => x.FMEAAnalysisId,
                        principalTable: "FMEAAnalyses",
                        principalColumn: "FMEAAnalysisId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FunctionalFailures",
                columns: table => new
                {
                    FunctionalFailureId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FunctionalRequirementId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureDefinition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalFailures", x => x.FunctionalFailureId);
                    table.ForeignKey(
                        name: "FK_FunctionalFailures_FunctionalRequirements_FunctionalRequirementId",
                        column: x => x.FunctionalRequirementId,
                        principalTable: "FunctionalRequirements",
                        principalColumn: "FunctionalRequirementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FMEAItems",
                columns: table => new
                {
                    FMEAItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FMEAAnalysisId = table.Column<int>(type: "int", nullable: false),
                    FunctionalFailureId = table.Column<int>(type: "int", nullable: true),
                    FailureModeId = table.Column<int>(type: "int", nullable: false),
                    FailureCauseId = table.Column<int>(type: "int", nullable: true),
                    FailureMechanismId = table.Column<int>(type: "int", nullable: true),
                    FailureEffectId = table.Column<int>(type: "int", nullable: true),
                    FailureConsequenceId = table.Column<int>(type: "int", nullable: true),
                    DetectionMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExistingControl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecommendedAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: true),
                    Occurrence = table.Column<int>(type: "int", nullable: true),
                    Detectability = table.Column<int>(type: "int", nullable: true),
                    MaintenanceStrategyTypeId = table.Column<int>(type: "int", nullable: true),
                    StandardReferenceId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FMEAItems", x => x.FMEAItemId);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FMEAAnalyses_FMEAAnalysisId",
                        column: x => x.FMEAAnalysisId,
                        principalTable: "FMEAAnalyses",
                        principalColumn: "FMEAAnalysisId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FailureCauses_FailureCauseId",
                        column: x => x.FailureCauseId,
                        principalTable: "FailureCauses",
                        principalColumn: "FailureCauseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FailureConsequences_FailureConsequenceId",
                        column: x => x.FailureConsequenceId,
                        principalTable: "FailureConsequences",
                        principalColumn: "FailureConsequenceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FailureEffects_FailureEffectId",
                        column: x => x.FailureEffectId,
                        principalTable: "FailureEffects",
                        principalColumn: "FailureEffectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FailureMechanisms_FailureMechanismId",
                        column: x => x.FailureMechanismId,
                        principalTable: "FailureMechanisms",
                        principalColumn: "FailureMechanismId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FailureModes_FailureModeId",
                        column: x => x.FailureModeId,
                        principalTable: "FailureModes",
                        principalColumn: "FailureModeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_FunctionalFailures_FunctionalFailureId",
                        column: x => x.FunctionalFailureId,
                        principalTable: "FunctionalFailures",
                        principalColumn: "FunctionalFailureId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                        column: x => x.MaintenanceStrategyTypeId,
                        principalTable: "MaintenanceStrategyTypes",
                        principalColumn: "MaintenanceStrategyTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FMEAItems_StandardReferences_StandardReferenceId",
                        column: x => x.StandardReferenceId,
                        principalTable: "StandardReferences",
                        principalColumn: "StandardReferenceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RCMDecisions",
                columns: table => new
                {
                    RCMDecisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RCMAnalysisId = table.Column<int>(type: "int", nullable: false),
                    FMEAItemId = table.Column<int>(type: "int", nullable: false),
                    DecisionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MaintenanceStrategyTypeId = table.Column<int>(type: "int", nullable: true),
                    RecommendedPMTemplateId = table.Column<int>(type: "int", nullable: true),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsequenceCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RCMDecisions", x => x.RCMDecisionId);
                    table.ForeignKey(
                        name: "FK_RCMDecisions_FMEAItems_FMEAItemId",
                        column: x => x.FMEAItemId,
                        principalTable: "FMEAItems",
                        principalColumn: "FMEAItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RCMDecisions_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                        column: x => x.MaintenanceStrategyTypeId,
                        principalTable: "MaintenanceStrategyTypes",
                        principalColumn: "MaintenanceStrategyTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RCMDecisions_PMTemplates_RecommendedPMTemplateId",
                        column: x => x.RecommendedPMTemplateId,
                        principalTable: "PMTemplates",
                        principalColumn: "PMTemplateId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RCMDecisions_RCMAnalyses_RCMAnalysisId",
                        column: x => x.RCMAnalysisId,
                        principalTable: "RCMAnalyses",
                        principalColumn: "RCMAnalysisId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FMEAAnalyses_EquipmentClassId_Status",
                table: "FMEAAnalyses",
                columns: new[] { "EquipmentClassId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FMEAAnalyses_SupersededByAnalysisId",
                table: "FMEAAnalyses",
                column: "SupersededByAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FailureCauseId",
                table: "FMEAItems",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FailureConsequenceId",
                table: "FMEAItems",
                column: "FailureConsequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FailureEffectId",
                table: "FMEAItems",
                column: "FailureEffectId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FailureMechanismId",
                table: "FMEAItems",
                column: "FailureMechanismId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FailureModeId",
                table: "FMEAItems",
                column: "FailureModeId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FMEAAnalysisId",
                table: "FMEAItems",
                column: "FMEAAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_FunctionalFailureId",
                table: "FMEAItems",
                column: "FunctionalFailureId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_MaintenanceStrategyTypeId",
                table: "FMEAItems",
                column: "MaintenanceStrategyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_FMEAItems_StandardReferenceId",
                table: "FMEAItems",
                column: "StandardReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalFailures_FunctionalRequirementId_Code",
                table: "FunctionalFailures",
                columns: new[] { "FunctionalRequirementId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalRequirements_EquipmentClassId_Code",
                table: "FunctionalRequirements",
                columns: new[] { "EquipmentClassId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RCMAnalyses_EquipmentClassId_Status",
                table: "RCMAnalyses",
                columns: new[] { "EquipmentClassId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RCMAnalyses_FMEAAnalysisId",
                table: "RCMAnalyses",
                column: "FMEAAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_RCMDecisions_FMEAItemId",
                table: "RCMDecisions",
                column: "FMEAItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RCMDecisions_MaintenanceStrategyTypeId",
                table: "RCMDecisions",
                column: "MaintenanceStrategyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RCMDecisions_RCMAnalysisId",
                table: "RCMDecisions",
                column: "RCMAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_RCMDecisions_RecommendedPMTemplateId",
                table: "RCMDecisions",
                column: "RecommendedPMTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RCMDecisions");

            migrationBuilder.DropTable(
                name: "FMEAItems");

            migrationBuilder.DropTable(
                name: "RCMAnalyses");

            migrationBuilder.DropTable(
                name: "FunctionalFailures");

            migrationBuilder.DropTable(
                name: "FMEAAnalyses");

            migrationBuilder.DropTable(
                name: "FunctionalRequirements");
        }
    }
}
