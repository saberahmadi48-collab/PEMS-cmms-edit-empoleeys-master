using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHelpStandardReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HelpStandardReferences",
                columns: table => new
                {
                    HelpStandardReferenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HelpArticleId = table.Column<int>(type: "int", nullable: false),
                    Identifier = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssuingOrganization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EditionOrYear = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfficialReferenceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedModule = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApplicabilityStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PEMSImplementationStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EvidencePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpStandardReferences", x => x.HelpStandardReferenceId);
                    table.ForeignKey(
                        name: "FK_HelpStandardReferences_HelpArticles_HelpArticleId",
                        column: x => x.HelpArticleId,
                        principalTable: "HelpArticles",
                        principalColumn: "HelpArticleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpStandardReferences_HelpArticleId",
                table: "HelpStandardReferences",
                column: "HelpArticleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HelpStandardReferences_Identifier",
                table: "HelpStandardReferences",
                column: "Identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HelpStandardReferences");
        }
    }
}
