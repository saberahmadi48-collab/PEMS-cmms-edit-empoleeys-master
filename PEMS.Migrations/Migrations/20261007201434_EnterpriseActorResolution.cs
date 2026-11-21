using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseActorResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntryCondition",
                table: "WorkflowStages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibilityKey",
                table: "WorkflowStages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionDetails",
                table: "WorkflowInstanceStages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionState",
                table: "WorkflowInstanceStages",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedDate",
                table: "WorkflowInstanceStages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowInstanceId",
                table: "PartCatalogueRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommodityGroupResponsibles",
                columns: table => new
                {
                    CommodityGroupResponsibleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResponsibilityKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CommodityGroupId = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommodityGroupResponsibles", x => x.CommodityGroupResponsibleId);
                    table.ForeignKey(
                        name: "FK_CommodityGroupResponsibles_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommodityGroupResponsibles_PartCatalogueCommodityGroups_CommodityGroupId",
                        column: x => x.CommodityGroupId,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommodityGroupResponsibles_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "PositionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActorResponsibilities",
                columns: table => new
                {
                    WorkflowActorResponsibilityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResponsibilityKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayNameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequiredModule = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RequiredAction = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScopeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ScopeValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ManagerLevel = table.Column<int>(type: "int", nullable: true),
                    SelectionStrategy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActorResponsibilities", x => x.WorkflowActorResponsibilityId);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstanceStageCandidates",
                columns: table => new
                {
                    WorkflowInstanceStageCandidateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceStageId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstanceStageCandidates", x => x.WorkflowInstanceStageCandidateId);
                    table.ForeignKey(
                        name: "FK_WorkflowInstanceStageCandidates_WorkflowInstanceStages_WorkflowInstanceStageId",
                        column: x => x.WorkflowInstanceStageId,
                        principalTable: "WorkflowInstanceStages",
                        principalColumn: "WorkflowInstanceStageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActorResponsibilityPositions",
                columns: table => new
                {
                    WorkflowActorResponsibilityPositionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowActorResponsibilityId = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActorResponsibilityPositions", x => x.WorkflowActorResponsibilityPositionId);
                    table.ForeignKey(
                        name: "FK_WorkflowActorResponsibilityPositions_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "PositionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowActorResponsibilityPositions_WorkflowActorResponsibilities_WorkflowActorResponsibilityId",
                        column: x => x.WorkflowActorResponsibilityId,
                        principalTable: "WorkflowActorResponsibilities",
                        principalColumn: "WorkflowActorResponsibilityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_WorkflowInstanceId",
                table: "PartCatalogueRequests",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommodityGroupResponsibles_CommodityGroupId",
                table: "CommodityGroupResponsibles",
                column: "CommodityGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CommodityGroupResponsibles_EmployeeId",
                table: "CommodityGroupResponsibles",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_CommodityGroupResponsibles_PositionId",
                table: "CommodityGroupResponsibles",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommodityGroupResponsibles_ResponsibilityKey_CommodityGroupId",
                table: "CommodityGroupResponsibles",
                columns: new[] { "ResponsibilityKey", "CommodityGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActorResponsibilities_ResponsibilityKey",
                table: "WorkflowActorResponsibilities",
                column: "ResponsibilityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActorResponsibilityPositions_PositionId",
                table: "WorkflowActorResponsibilityPositions",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActorResponsibilityPositions_WorkflowActorResponsibilityId_PositionId",
                table: "WorkflowActorResponsibilityPositions",
                columns: new[] { "WorkflowActorResponsibilityId", "PositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceStageCandidates_EmployeeId_IsSelected",
                table: "WorkflowInstanceStageCandidates",
                columns: new[] { "EmployeeId", "IsSelected" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceStageCandidates_WorkflowInstanceStageId_EmployeeId",
                table: "WorkflowInstanceStageCandidates",
                columns: new[] { "WorkflowInstanceStageId", "EmployeeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommodityGroupResponsibles");

            migrationBuilder.DropTable(
                name: "WorkflowActorResponsibilityPositions");

            migrationBuilder.DropTable(
                name: "WorkflowInstanceStageCandidates");

            migrationBuilder.DropTable(
                name: "WorkflowActorResponsibilities");

            migrationBuilder.DropIndex(
                name: "IX_PartCatalogueRequests_WorkflowInstanceId",
                table: "PartCatalogueRequests");

            migrationBuilder.DropColumn(
                name: "EntryCondition",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "ResponsibilityKey",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "ResolutionDetails",
                table: "WorkflowInstanceStages");

            migrationBuilder.DropColumn(
                name: "ResolutionState",
                table: "WorkflowInstanceStages");

            migrationBuilder.DropColumn(
                name: "ResolvedDate",
                table: "WorkflowInstanceStages");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "PartCatalogueRequests");
        }
    }
}
