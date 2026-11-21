using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LicensingSeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenseEvent",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    UtcAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DataHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PrevHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntryHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseEvent", x => x.Seq);
                });

            migrationBuilder.CreateTable(
                name: "LicenseSeal",
                columns: table => new
                {
                    LicenseSealRecordId = table.Column<int>(type: "int", nullable: false),
                    Bundle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseSeal", x => x.LicenseSealRecordId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseEvent");

            migrationBuilder.DropTable(
                name: "LicenseSeal");
        }
    }
}
