using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkOrderGovernanceOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginatingDisciplineId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginatingOrganization",
                table: "WorkOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginatingRequestedById",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponsibleDisciplineId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleOrganization",
                table: "WorkOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationDomain",
                table: "Departments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OriginatingDisciplineId",
                table: "WorkOrders",
                column: "OriginatingDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ResponsibleDisciplineId",
                table: "WorkOrders",
                column: "ResponsibleDisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Disciplines_OriginatingDisciplineId",
                table: "WorkOrders",
                column: "OriginatingDisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Disciplines_ResponsibleDisciplineId",
                table: "WorkOrders",
                column: "ResponsibleDisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Disciplines_OriginatingDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Disciplines_ResponsibleDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_OriginatingDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ResponsibleDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OriginatingDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OriginatingOrganization",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OriginatingRequestedById",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ResponsibleDisciplineId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ResponsibleOrganization",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OrganizationDomain",
                table: "Departments");
        }
    }
}
