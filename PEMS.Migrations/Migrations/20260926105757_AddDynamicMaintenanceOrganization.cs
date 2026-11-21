using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicMaintenanceOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NodeKind",
                table: "Teams",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Team");

            migrationBuilder.AddColumn<int>(
                name: "ParentTeamId",
                table: "Teams",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Positions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "Positions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentMaintenanceGroupId",
                table: "MaintenanceGroups",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeAssignments",
                columns: table => new
                {
                    EmployeeAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PositionId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_EmployeeAssignments", x => x.EmployeeAssignmentId);
                    table.ForeignKey(
                        name: "FK_EmployeeAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAssignments_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "PositionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ParentTeamId",
                table: "Teams",
                column: "ParentTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_TeamId",
                table: "Positions",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceGroups_ParentMaintenanceGroupId",
                table: "MaintenanceGroups",
                column: "ParentMaintenanceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssignments_EmployeeId",
                table: "EmployeeAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssignments_PositionId_ActiveOnly",
                table: "EmployeeAssignments",
                column: "PositionId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceGroups_MaintenanceGroups_ParentMaintenanceGroupId",
                table: "MaintenanceGroups",
                column: "ParentMaintenanceGroupId",
                principalTable: "MaintenanceGroups",
                principalColumn: "MaintenanceGroupId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Positions_Teams_TeamId",
                table: "Positions",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "TeamId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Teams_ParentTeamId",
                table: "Teams",
                column: "ParentTeamId",
                principalTable: "Teams",
                principalColumn: "TeamId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceGroups_MaintenanceGroups_ParentMaintenanceGroupId",
                table: "MaintenanceGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_Positions_Teams_TeamId",
                table: "Positions");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Teams_ParentTeamId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "EmployeeAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Teams_ParentTeamId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Positions_TeamId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceGroups_ParentMaintenanceGroupId",
                table: "MaintenanceGroups");

            migrationBuilder.DropColumn(
                name: "NodeKind",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ParentTeamId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ParentMaintenanceGroupId",
                table: "MaintenanceGroups");
        }
    }
}
