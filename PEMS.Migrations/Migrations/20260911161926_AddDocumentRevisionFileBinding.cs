using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRevisionFileBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentRevisionId",
                table: "DocumentAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "DocumentAttachments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileRole",
                table: "DocumentAttachments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOfficial",
                table: "DocumentAttachments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_DocumentRevisionId",
                table: "DocumentAttachments",
                column: "DocumentRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAttachments_DocumentRevisions_DocumentRevisionId",
                table: "DocumentAttachments",
                column: "DocumentRevisionId",
                principalTable: "DocumentRevisions",
                principalColumn: "RevisionId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentAttachments_DocumentRevisions_DocumentRevisionId",
                table: "DocumentAttachments");

            migrationBuilder.DropIndex(
                name: "IX_DocumentAttachments_DocumentRevisionId",
                table: "DocumentAttachments");

            migrationBuilder.DropColumn(
                name: "DocumentRevisionId",
                table: "DocumentAttachments");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "DocumentAttachments");

            migrationBuilder.DropColumn(
                name: "FileRole",
                table: "DocumentAttachments");

            migrationBuilder.DropColumn(
                name: "IsOfficial",
                table: "DocumentAttachments");
        }
    }
}
