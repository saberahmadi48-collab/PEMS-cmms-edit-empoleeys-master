using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowPlatformPhase1Hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeSummary",
                table: "WorkflowVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClonedFromVersionId",
                table: "WorkflowVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredDate",
                table: "WorkflowVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidatedById",
                table: "WorkflowVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedDate",
                table: "WorkflowVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForbidInitiator",
                table: "WorkflowTransitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequiredPermission",
                table: "WorkflowTransitions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationRole",
                table: "WorkflowStages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationKey",
                table: "WorkflowStages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponsibleEmployeeId",
                table: "WorkflowStages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityKey",
                table: "WorkflowInstances",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowBindingId",
                table: "WorkflowInstances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowBindings",
                columns: table => new
                {
                    WorkflowBindingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkflowDefinitionId = table.Column<int>(type: "int", nullable: false),
                    PinnedVersionId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    DeactivatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBindings", x => x.WorkflowBindingId);
                    table.ForeignKey(
                        name: "FK_WorkflowBindings_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "WorkflowDefinitionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowBindings_WorkflowVersions_PinnedVersionId",
                        column: x => x.PinnedVersionId,
                        principalTable: "WorkflowVersions",
                        principalColumn: "WorkflowVersionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowConfigAudits",
                columns: table => new
                {
                    WorkflowConfigAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WorkflowDefinitionId = table.Column<int>(type: "int", nullable: true),
                    WorkflowVersionId = table.Column<int>(type: "int", nullable: true),
                    WorkflowBindingId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousState = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CurrentState = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowConfigAudits", x => x.WorkflowConfigAuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_EntityKey",
                table: "WorkflowInstances",
                column: "EntityKey");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBindings_Module_EntityType_Context_IsActive",
                table: "WorkflowBindings",
                columns: new[] { "Module", "EntityType", "Context", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBindings_PinnedVersionId",
                table: "WorkflowBindings",
                column: "PinnedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBindings_WorkflowDefinitionId",
                table: "WorkflowBindings",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowConfigAudits_WorkflowDefinitionId_Timestamp",
                table: "WorkflowConfigAudits",
                columns: new[] { "WorkflowDefinitionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowConfigAudits_WorkflowVersionId",
                table: "WorkflowConfigAudits",
                column: "WorkflowVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowBindings");

            migrationBuilder.DropTable(
                name: "WorkflowConfigAudits");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_EntityKey",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ChangeSummary",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "ClonedFromVersionId",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "RetiredDate",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "ValidatedById",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "ValidatedDate",
                table: "WorkflowVersions");

            migrationBuilder.DropColumn(
                name: "ForbidInitiator",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "RequiredPermission",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "EscalationRole",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "NotificationKey",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "ResponsibleEmployeeId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "EntityKey",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "WorkflowBindingId",
                table: "WorkflowInstances");
        }
    }
}
