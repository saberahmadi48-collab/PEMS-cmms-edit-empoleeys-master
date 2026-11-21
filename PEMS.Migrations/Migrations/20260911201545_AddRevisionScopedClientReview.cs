using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionScopedClientReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientReviewComment",
                table: "DocumentRevisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientReviewDate",
                table: "DocumentRevisions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientReviewLetterId",
                table: "DocumentRevisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientReviewStatus",
                table: "DocumentRevisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_ClientReviewLetterId",
                table: "DocumentRevisions",
                column: "ClientReviewLetterId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRevisions_Letters_ClientReviewLetterId",
                table: "DocumentRevisions",
                column: "ClientReviewLetterId",
                principalTable: "Letters",
                principalColumn: "LetterId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRevisions_Letters_ClientReviewLetterId",
                table: "DocumentRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentRevisions_ClientReviewLetterId",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "ClientReviewComment",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "ClientReviewDate",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "ClientReviewLetterId",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "ClientReviewStatus",
                table: "DocumentRevisions");
        }
    }
}
