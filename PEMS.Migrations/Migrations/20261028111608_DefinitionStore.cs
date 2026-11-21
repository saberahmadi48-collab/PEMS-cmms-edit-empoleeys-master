using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DefinitionStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefinitionStamps",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionStamps", x => x.Kind);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionReferences",
                columns: table => new
                {
                    DefinitionReferenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromVersionId = table.Column<int>(type: "int", nullable: false),
                    ToKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionReferences", x => x.DefinitionReferenceId);
                });

            migrationBuilder.CreateTable(
                name: "Definitions",
                columns: table => new
                {
                    DefinitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AppKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    TitleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DescriptionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedVersionId = table.Column<int>(type: "int", nullable: true),
                    ShippedVersionId = table.Column<int>(type: "int", nullable: true),
                    IsCustomized = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Definitions", x => x.DefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "DefinitionVersions",
                columns: table => new
                {
                    DefinitionVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClonedFromVersionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefinitionVersions", x => x.DefinitionVersionId);
                    table.ForeignKey(
                        name: "FK_DefinitionVersions_Definitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "Definitions",
                        principalColumn: "DefinitionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionReferences_FromVersionId",
                table: "DefinitionReferences",
                column: "FromVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionReferences_Target",
                table: "DefinitionReferences",
                columns: new[] { "ToKind", "ToKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Definitions_PublishedVersionId",
                table: "Definitions",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_Definitions_AppKey_Kind_Key",
                table: "Definitions",
                columns: new[] { "AppKey", "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefinitionVersions_ContentHash",
                table: "DefinitionVersions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "UX_DefinitionVersions_Definition_Number",
                table: "DefinitionVersions",
                columns: new[] { "DefinitionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DefinitionVersions_OneOpenDraft",
                table: "DefinitionVersions",
                column: "DefinitionId",
                unique: true,
                filter: "[Status] IN (0, 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_DefinitionReferences_DefinitionVersions_FromVersionId",
                table: "DefinitionReferences",
                column: "FromVersionId",
                principalTable: "DefinitionVersions",
                principalColumn: "DefinitionVersionId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Definitions_DefinitionVersions_PublishedVersionId",
                table: "Definitions",
                column: "PublishedVersionId",
                principalTable: "DefinitionVersions",
                principalColumn: "DefinitionVersionId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Definitions_DefinitionVersions_PublishedVersionId",
                table: "Definitions");

            migrationBuilder.DropTable(
                name: "DefinitionReferences");

            migrationBuilder.DropTable(
                name: "DefinitionStamps");

            migrationBuilder.DropTable(
                name: "DefinitionVersions");

            migrationBuilder.DropTable(
                name: "Definitions");
        }
    }
}
