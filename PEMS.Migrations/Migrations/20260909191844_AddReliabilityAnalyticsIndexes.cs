using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReliabilityAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "WorkOrders",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_CompletionDate",
                table: "WorkOrders",
                columns: new[] { "Status", "CompletionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_IsActive_FailureDate",
                table: "FailureRecords",
                columns: new[] { "IsActive", "FailureDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Status_CompletionDate",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_FailureRecords_IsActive_FailureDate",
                table: "FailureRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
