using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMrsImportMetadataAndSourceDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceAttachmentId",
                table: "MrsImportBatches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MrsImportRowMetadata",
                columns: table => new
                {
                    MrsImportRowMetadataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MrsImportRowId = table.Column<int>(type: "int", nullable: false),
                    ColumnIndex = table.Column<int>(type: "int", nullable: false),
                    OriginalHeader = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CanonicalField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MrsImportRowMetadata", x => x.MrsImportRowMetadataId);
                    table.ForeignKey(
                        name: "FK_MrsImportRowMetadata_MrsImportRows_MrsImportRowId",
                        column: x => x.MrsImportRowId,
                        principalTable: "MrsImportRows",
                        principalColumn: "MrsImportRowId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportBatches_SourceAttachmentId",
                table: "MrsImportBatches",
                column: "SourceAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRowMetadata_MrsImportRowId_CanonicalField",
                table: "MrsImportRowMetadata",
                columns: new[] { "MrsImportRowId", "CanonicalField" });

            migrationBuilder.AddForeignKey(
                name: "FK_MrsImportBatches_DocumentAttachments_SourceAttachmentId",
                table: "MrsImportBatches",
                column: "SourceAttachmentId",
                principalTable: "DocumentAttachments",
                principalColumn: "AttachmentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MrsImportBatches_DocumentAttachments_SourceAttachmentId",
                table: "MrsImportBatches");

            migrationBuilder.DropTable(
                name: "MrsImportRowMetadata");

            migrationBuilder.DropIndex(
                name: "IX_MrsImportBatches_SourceAttachmentId",
                table: "MrsImportBatches");

            migrationBuilder.DropColumn(
                name: "SourceAttachmentId",
                table: "MrsImportBatches");
        }
    }
}
