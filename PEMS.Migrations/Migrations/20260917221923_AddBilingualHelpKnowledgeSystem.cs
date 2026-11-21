using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBilingualHelpKnowledgeSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HelpCategories",
                columns: table => new
                {
                    HelpCategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IconCssClass = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpCategories", x => x.HelpCategoryId);
                });

            migrationBuilder.CreateTable(
                name: "HelpArticles",
                columns: table => new
                {
                    HelpArticleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HelpCategoryId = table.Column<int>(type: "int", nullable: false),
                    ArticleType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    HelpKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RelevantRoles = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpArticles", x => x.HelpArticleId);
                    table.ForeignKey(
                        name: "FK_HelpArticles_HelpCategories_HelpCategoryId",
                        column: x => x.HelpCategoryId,
                        principalTable: "HelpCategories",
                        principalColumn: "HelpCategoryId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HelpArticleTranslations",
                columns: table => new
                {
                    HelpArticleTranslationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpArticleId = table.Column<int>(type: "int", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SearchKeywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpArticleTranslations", x => x.HelpArticleTranslationId);
                    table.ForeignKey(
                        name: "FK_HelpArticleTranslations_HelpArticles_HelpArticleId",
                        column: x => x.HelpArticleId,
                        principalTable: "HelpArticles",
                        principalColumn: "HelpArticleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HelpRelatedArticles",
                columns: table => new
                {
                    HelpRelatedArticleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpArticleId = table.Column<int>(type: "int", nullable: false),
                    RelatedHelpArticleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRelatedArticles", x => x.HelpRelatedArticleId);
                    table.ForeignKey(
                        name: "FK_HelpRelatedArticles_HelpArticles_HelpArticleId",
                        column: x => x.HelpArticleId,
                        principalTable: "HelpArticles",
                        principalColumn: "HelpArticleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HelpRelatedArticles_HelpArticles_RelatedHelpArticleId",
                        column: x => x.RelatedHelpArticleId,
                        principalTable: "HelpArticles",
                        principalColumn: "HelpArticleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpArticles_Code",
                table: "HelpArticles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpArticles_HelpCategoryId",
                table: "HelpArticles",
                column: "HelpCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_HelpArticles_HelpKey",
                table: "HelpArticles",
                column: "HelpKey");

            migrationBuilder.CreateIndex(
                name: "IX_HelpArticleTranslations_HelpArticleId_LanguageCode",
                table: "HelpArticleTranslations",
                columns: new[] { "HelpArticleId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpCategories_Code",
                table: "HelpCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRelatedArticles_HelpArticleId_RelatedHelpArticleId",
                table: "HelpRelatedArticles",
                columns: new[] { "HelpArticleId", "RelatedHelpArticleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpRelatedArticles_RelatedHelpArticleId",
                table: "HelpRelatedArticles",
                column: "RelatedHelpArticleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HelpArticleTranslations");

            migrationBuilder.DropTable(
                name: "HelpRelatedArticles");

            migrationBuilder.DropTable(
                name: "HelpArticles");

            migrationBuilder.DropTable(
                name: "HelpCategories");
        }
    }
}
