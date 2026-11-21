using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobPlanAndMaintenanceTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JobPlanId",
                table: "MaintenancePlans",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobPlans",
                columns: table => new
                {
                    JobPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobPlanNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedDurationHours = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SafetyRequirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPlans", x => x.JobPlanId);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTasks",
                columns: table => new
                {
                    WorkOrderTaskId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    TaskNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTasks", x => x.WorkOrderTaskId);
                    table.ForeignKey(
                        name: "FK_WorkOrderTasks_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobPlanRequiredMaterials",
                columns: table => new
                {
                    JobPlanRequiredMaterialId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobPlanId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPlanRequiredMaterials", x => x.JobPlanRequiredMaterialId);
                    table.ForeignKey(
                        name: "FK_JobPlanRequiredMaterials_JobPlans_JobPlanId",
                        column: x => x.JobPlanId,
                        principalTable: "JobPlans",
                        principalColumn: "JobPlanId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobPlanRequiredMaterials_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobPlanRequiredMaterials_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId");
                });

            migrationBuilder.CreateTable(
                name: "JobPlanRequiredSkills",
                columns: table => new
                {
                    JobPlanRequiredSkillId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobPlanId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    MinimumProficiencyLevel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPlanRequiredSkills", x => x.JobPlanRequiredSkillId);
                    table.ForeignKey(
                        name: "FK_JobPlanRequiredSkills_JobPlans_JobPlanId",
                        column: x => x.JobPlanId,
                        principalTable: "JobPlans",
                        principalColumn: "JobPlanId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobPlanRequiredSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "SkillId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTasks",
                columns: table => new
                {
                    MaintenanceTaskId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobPlanId = table.Column<int>(type: "int", nullable: false),
                    TaskNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedDurationMinutes = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTasks", x => x.MaintenanceTaskId);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_JobPlans_JobPlanId",
                        column: x => x.JobPlanId,
                        principalTable: "JobPlans",
                        principalColumn: "JobPlanId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_JobPlanId",
                table: "MaintenancePlans",
                column: "JobPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlanRequiredMaterials_JobPlanId",
                table: "JobPlanRequiredMaterials",
                column: "JobPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlanRequiredMaterials_MaterialItemId",
                table: "JobPlanRequiredMaterials",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlanRequiredMaterials_UomId",
                table: "JobPlanRequiredMaterials",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlanRequiredSkills_JobPlanId",
                table: "JobPlanRequiredSkills",
                column: "JobPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPlanRequiredSkills_SkillId",
                table: "JobPlanRequiredSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_JobPlanId",
                table: "MaintenanceTasks",
                column: "JobPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTasks_WorkOrderId",
                table: "WorkOrderTasks",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenancePlans_JobPlans_JobPlanId",
                table: "MaintenancePlans",
                column: "JobPlanId",
                principalTable: "JobPlans",
                principalColumn: "JobPlanId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenancePlans_JobPlans_JobPlanId",
                table: "MaintenancePlans");

            migrationBuilder.DropTable(
                name: "JobPlanRequiredMaterials");

            migrationBuilder.DropTable(
                name: "JobPlanRequiredSkills");

            migrationBuilder.DropTable(
                name: "MaintenanceTasks");

            migrationBuilder.DropTable(
                name: "WorkOrderTasks");

            migrationBuilder.DropTable(
                name: "JobPlans");

            migrationBuilder.DropIndex(
                name: "IX_MaintenancePlans_JobPlanId",
                table: "MaintenancePlans");

            migrationBuilder.DropColumn(
                name: "JobPlanId",
                table: "MaintenancePlans");
        }
    }
}
