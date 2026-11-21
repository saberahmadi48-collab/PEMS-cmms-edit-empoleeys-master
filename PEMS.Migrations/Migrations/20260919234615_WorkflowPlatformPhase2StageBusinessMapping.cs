using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowPlatformPhase2StageBusinessMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "WorkflowTransitions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelFa",
                table: "WorkflowTransitions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessStatus",
                table: "WorkflowStages",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameFa",
                table: "WorkflowStages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "LabelFa",
                table: "WorkflowTransitions");

            migrationBuilder.DropColumn(
                name: "BusinessStatus",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "NameFa",
                table: "WorkflowStages");
        }
    }
}
