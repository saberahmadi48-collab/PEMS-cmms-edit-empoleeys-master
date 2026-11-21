using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scrum807PackagesAndViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppliedPackages",
                columns: table => new
                {
                    AppliedPackageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BackupPoint = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    AppliedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedPackages", x => x.AppliedPackageId);
                });

            migrationBuilder.CreateTable(
                name: "GridSavedViews",
                columns: table => new
                {
                    GridSavedViewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ViewsKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridSavedViews", x => x.GridSavedViewId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedPackages_PackageHash",
                table: "AppliedPackages",
                column: "PackageHash");

            migrationBuilder.CreateIndex(
                name: "IX_GridSavedViews_ViewsKey_ScopeKind_ScopeKey_Name",
                table: "GridSavedViews",
                columns: new[] { "ViewsKey", "ScopeKind", "ScopeKey", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppliedPackages");

            migrationBuilder.DropTable(
                name: "GridSavedViews");
        }
    }
}
