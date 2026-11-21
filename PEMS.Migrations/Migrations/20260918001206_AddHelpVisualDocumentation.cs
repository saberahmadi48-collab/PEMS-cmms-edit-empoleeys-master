using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpVisualDocumentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HelpVisuals",
                columns: table => new
                {
                    HelpVisualId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpArticleId = table.Column<int>(type: "int", nullable: false),
                    VisualType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    SourceWorkflowModule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceWorkflowCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaptionEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CaptionFa = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    LegendEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LegendFa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AccessibilityDescriptionEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AccessibilityDescriptionFa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RelatedHelpKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpVisuals", x => x.HelpVisualId);
                    table.ForeignKey(
                        name: "FK_HelpVisuals_HelpArticles_HelpArticleId",
                        column: x => x.HelpArticleId,
                        principalTable: "HelpArticles",
                        principalColumn: "HelpArticleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpVisuals_HelpArticleId",
                table: "HelpVisuals",
                column: "HelpArticleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HelpVisuals");
        }
    }
}
