using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentClassificationFailureTaxonomyAndPMTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureCauseId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureModeId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentClassId",
                table: "JobPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LOTORequired",
                table: "JobPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceActivityId",
                table: "JobPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermitRequired",
                table: "JobPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StandardReferenceId",
                table: "JobPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureCauseId",
                table: "FailureRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureModeId",
                table: "FailureRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationConfidence",
                table: "Equipments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationSource",
                table: "Equipments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentEquipmentClassId",
                table: "EquipmentClasses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FailureCauses",
                columns: table => new
                {
                    FailureCauseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureCauses", x => x.FailureCauseId);
                });

            migrationBuilder.CreateTable(
                name: "FailureConsequences",
                columns: table => new
                {
                    FailureConsequenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureConsequences", x => x.FailureConsequenceId);
                });

            migrationBuilder.CreateTable(
                name: "FailureEffects",
                columns: table => new
                {
                    FailureEffectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureEffects", x => x.FailureEffectId);
                });

            migrationBuilder.CreateTable(
                name: "FailureMechanisms",
                columns: table => new
                {
                    FailureMechanismId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureMechanisms", x => x.FailureMechanismId);
                });

            migrationBuilder.CreateTable(
                name: "PMTemplates",
                columns: table => new
                {
                    PMTemplateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: true),
                    MaintenanceStrategyTypeId = table.Column<int>(type: "int", nullable: true),
                    MaintenanceTypeId = table.Column<int>(type: "int", nullable: true),
                    JobPlanId = table.Column<int>(type: "int", nullable: true),
                    TriggerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: true),
                    MeterType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    MeterInterval = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ApplicableCriticalityClasses = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StandardReferenceId = table.Column<int>(type: "int", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PMTemplates", x => x.PMTemplateId);
                    table.ForeignKey(
                        name: "FK_PMTemplates_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PMTemplates_JobPlans_JobPlanId",
                        column: x => x.JobPlanId,
                        principalTable: "JobPlans",
                        principalColumn: "JobPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PMTemplates_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                        column: x => x.MaintenanceStrategyTypeId,
                        principalTable: "MaintenanceStrategyTypes",
                        principalColumn: "MaintenanceStrategyTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PMTemplates_MaintenanceTypes_MaintenanceTypeId",
                        column: x => x.MaintenanceTypeId,
                        principalTable: "MaintenanceTypes",
                        principalColumn: "MaintenanceTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PMTemplates_StandardReferences_StandardReferenceId",
                        column: x => x.StandardReferenceId,
                        principalTable: "StandardReferences",
                        principalColumn: "StandardReferenceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FailureModes",
                columns: table => new
                {
                    FailureModeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: true),
                    FailureEffectId = table.Column<int>(type: "int", nullable: true),
                    FailureConsequenceId = table.Column<int>(type: "int", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureModes", x => x.FailureModeId);
                    table.ForeignKey(
                        name: "FK_FailureModes_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FailureModes_FailureConsequences_FailureConsequenceId",
                        column: x => x.FailureConsequenceId,
                        principalTable: "FailureConsequences",
                        principalColumn: "FailureConsequenceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FailureModes_FailureEffects_FailureEffectId",
                        column: x => x.FailureEffectId,
                        principalTable: "FailureEffects",
                        principalColumn: "FailureEffectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FailureCauseMechanisms",
                columns: table => new
                {
                    FailureCauseId = table.Column<int>(type: "int", nullable: false),
                    FailureMechanismId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureCauseMechanisms", x => new { x.FailureCauseId, x.FailureMechanismId });
                    table.ForeignKey(
                        name: "FK_FailureCauseMechanisms_FailureCauses_FailureCauseId",
                        column: x => x.FailureCauseId,
                        principalTable: "FailureCauses",
                        principalColumn: "FailureCauseId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FailureCauseMechanisms_FailureMechanisms_FailureMechanismId",
                        column: x => x.FailureMechanismId,
                        principalTable: "FailureMechanisms",
                        principalColumn: "FailureMechanismId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FailureModeCauses",
                columns: table => new
                {
                    FailureModeId = table.Column<int>(type: "int", nullable: false),
                    FailureCauseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureModeCauses", x => new { x.FailureModeId, x.FailureCauseId });
                    table.ForeignKey(
                        name: "FK_FailureModeCauses_FailureCauses_FailureCauseId",
                        column: x => x.FailureCauseId,
                        principalTable: "FailureCauses",
                        principalColumn: "FailureCauseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FailureModeCauses_FailureModes_FailureModeId",
                        column: x => x.FailureModeId,
                        principalTable: "FailureModes",
                        principalColumn: "FailureModeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FailureCauseId",
                table: "WorkOrders",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FailureModeId",
                table: "WorkOrders",
                column: "FailureModeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlans_EquipmentClassId",
                table: "JobPlans",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlans_MaintenanceActivityId",
                table: "JobPlans",
                column: "MaintenanceActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlans_StandardReferenceId",
                table: "JobPlans",
                column: "StandardReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_FailureCauseId",
                table: "FailureRecords",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_FailureModeId",
                table: "FailureRecords",
                column: "FailureModeId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentClasses_ParentEquipmentClassId",
                table: "EquipmentClasses",
                column: "ParentEquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureCauseMechanisms_FailureMechanismId",
                table: "FailureCauseMechanisms",
                column: "FailureMechanismId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureCauses_Code",
                table: "FailureCauses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureConsequences_Code",
                table: "FailureConsequences",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureEffects_Code",
                table: "FailureEffects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureMechanisms_Code",
                table: "FailureMechanisms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureModeCauses_FailureCauseId",
                table: "FailureModeCauses",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureModes_Code",
                table: "FailureModes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailureModes_EquipmentClassId",
                table: "FailureModes",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureModes_FailureConsequenceId",
                table: "FailureModes",
                column: "FailureConsequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureModes_FailureEffectId",
                table: "FailureModes",
                column: "FailureEffectId");

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_Code",
                table: "PMTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_EquipmentClassId",
                table: "PMTemplates",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_JobPlanId",
                table: "PMTemplates",
                column: "JobPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_MaintenanceStrategyTypeId",
                table: "PMTemplates",
                column: "MaintenanceStrategyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_MaintenanceTypeId",
                table: "PMTemplates",
                column: "MaintenanceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PMTemplates_StandardReferenceId",
                table: "PMTemplates",
                column: "StandardReferenceId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentClasses_EquipmentClasses_ParentEquipmentClassId",
                table: "EquipmentClasses",
                column: "ParentEquipmentClassId",
                principalTable: "EquipmentClasses",
                principalColumn: "EquipmentClassId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FailureRecords_FailureCauses_FailureCauseId",
                table: "FailureRecords",
                column: "FailureCauseId",
                principalTable: "FailureCauses",
                principalColumn: "FailureCauseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FailureRecords_FailureModes_FailureModeId",
                table: "FailureRecords",
                column: "FailureModeId",
                principalTable: "FailureModes",
                principalColumn: "FailureModeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPlans_EquipmentClasses_EquipmentClassId",
                table: "JobPlans",
                column: "EquipmentClassId",
                principalTable: "EquipmentClasses",
                principalColumn: "EquipmentClassId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPlans_MaintenanceActivities_MaintenanceActivityId",
                table: "JobPlans",
                column: "MaintenanceActivityId",
                principalTable: "MaintenanceActivities",
                principalColumn: "MaintenanceActivityId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobPlans_StandardReferences_StandardReferenceId",
                table: "JobPlans",
                column: "StandardReferenceId",
                principalTable: "StandardReferences",
                principalColumn: "StandardReferenceId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_FailureCauses_FailureCauseId",
                table: "WorkOrders",
                column: "FailureCauseId",
                principalTable: "FailureCauses",
                principalColumn: "FailureCauseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_FailureModes_FailureModeId",
                table: "WorkOrders",
                column: "FailureModeId",
                principalTable: "FailureModes",
                principalColumn: "FailureModeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentClasses_EquipmentClasses_ParentEquipmentClassId",
                table: "EquipmentClasses");

            migrationBuilder.DropForeignKey(
                name: "FK_FailureRecords_FailureCauses_FailureCauseId",
                table: "FailureRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_FailureRecords_FailureModes_FailureModeId",
                table: "FailureRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPlans_EquipmentClasses_EquipmentClassId",
                table: "JobPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPlans_MaintenanceActivities_MaintenanceActivityId",
                table: "JobPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_JobPlans_StandardReferences_StandardReferenceId",
                table: "JobPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_FailureCauses_FailureCauseId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_FailureModes_FailureModeId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "FailureCauseMechanisms");

            migrationBuilder.DropTable(
                name: "FailureModeCauses");

            migrationBuilder.DropTable(
                name: "PMTemplates");

            migrationBuilder.DropTable(
                name: "FailureMechanisms");

            migrationBuilder.DropTable(
                name: "FailureCauses");

            migrationBuilder.DropTable(
                name: "FailureModes");

            migrationBuilder.DropTable(
                name: "FailureConsequences");

            migrationBuilder.DropTable(
                name: "FailureEffects");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_FailureCauseId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_FailureModeId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_JobPlans_EquipmentClassId",
                table: "JobPlans");

            migrationBuilder.DropIndex(
                name: "IX_JobPlans_MaintenanceActivityId",
                table: "JobPlans");

            migrationBuilder.DropIndex(
                name: "IX_JobPlans_StandardReferenceId",
                table: "JobPlans");

            migrationBuilder.DropIndex(
                name: "IX_FailureRecords_FailureCauseId",
                table: "FailureRecords");

            migrationBuilder.DropIndex(
                name: "IX_FailureRecords_FailureModeId",
                table: "FailureRecords");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentClasses_ParentEquipmentClassId",
                table: "EquipmentClasses");

            migrationBuilder.DropColumn(
                name: "FailureCauseId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "FailureModeId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "EquipmentClassId",
                table: "JobPlans");

            migrationBuilder.DropColumn(
                name: "LOTORequired",
                table: "JobPlans");

            migrationBuilder.DropColumn(
                name: "MaintenanceActivityId",
                table: "JobPlans");

            migrationBuilder.DropColumn(
                name: "PermitRequired",
                table: "JobPlans");

            migrationBuilder.DropColumn(
                name: "StandardReferenceId",
                table: "JobPlans");

            migrationBuilder.DropColumn(
                name: "FailureCauseId",
                table: "FailureRecords");

            migrationBuilder.DropColumn(
                name: "FailureModeId",
                table: "FailureRecords");

            migrationBuilder.DropColumn(
                name: "ClassificationConfidence",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "ClassificationSource",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "ParentEquipmentClassId",
                table: "EquipmentClasses");
        }
    }
}
