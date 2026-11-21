using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitExecutionGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionGateDecisions",
                columns: table => new
                {
                    ExecutionGateDecisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    ActorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockingReasonsSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEmergencyOverride = table.Column<bool>(type: "bit", nullable: false),
                    EmergencyReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecisionDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionGateDecisions", x => x.ExecutionGateDecisionId);
                    table.ForeignKey(
                        name: "FK_ExecutionGateDecisions_Employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExecutionGateDecisions_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermitExecutionRequirements",
                columns: table => new
                {
                    PermitExecutionRequirementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequireIsolationVerified = table.Column<bool>(type: "bit", nullable: false),
                    RequireValidGasTest = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitExecutionRequirements", x => x.PermitExecutionRequirementId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionGateDecisions_ActorEmployeeId",
                table: "ExecutionGateDecisions",
                column: "ActorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionGateDecisions_PermitId",
                table: "ExecutionGateDecisions",
                column: "PermitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionGateDecisions");

            migrationBuilder.DropTable(
                name: "PermitExecutionRequirements");
        }
    }
}
