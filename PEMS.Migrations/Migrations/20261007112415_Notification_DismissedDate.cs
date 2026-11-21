using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notification_DismissedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DismissedDate",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DismissedDate_CreatedDate",
                table: "Notifications",
                columns: new[] { "UserId", "DismissedDate", "CreatedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_DismissedDate_CreatedDate",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DismissedDate",
                table: "Notifications");
        }
    }
}
