using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedFinish",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedStart",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_PlannedStart_PlannedFinish",
                table: "WorkOrders",
                columns: new[] { "PlannedStart", "PlannedFinish" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_PlannedStart_PlannedFinish",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PlannedFinish",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PlannedStart",
                table: "WorkOrders");
        }
    }
}
