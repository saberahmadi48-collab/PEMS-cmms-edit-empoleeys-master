using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BarcodeIdentities",
                columns: table => new
                {
                    BarcodeIdentityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Symbology = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    LastPrintedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisabledDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisabledById = table.Column<int>(type: "int", nullable: true),
                    DisableReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevokedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedById = table.Column<int>(type: "int", nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplacedByBarcodeIdentityId = table.Column<int>(type: "int", nullable: true),
                    ReplacesBarcodeIdentityId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarcodeIdentities", x => x.BarcodeIdentityId);
                    table.ForeignKey(
                        name: "FK_BarcodeIdentities_BarcodeIdentities_ReplacedByBarcodeIdentityId",
                        column: x => x.ReplacedByBarcodeIdentityId,
                        principalTable: "BarcodeIdentities",
                        principalColumn: "BarcodeIdentityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarcodeIdentities_BarcodeIdentities_ReplacesBarcodeIdentityId",
                        column: x => x.ReplacesBarcodeIdentityId,
                        principalTable: "BarcodeIdentities",
                        principalColumn: "BarcodeIdentityId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarcodeIdentities_Employees_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarcodeIdentities_Employees_DisabledById",
                        column: x => x.DisabledById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarcodeIdentities_Employees_RevokedById",
                        column: x => x.RevokedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_Code",
                table: "BarcodeIdentities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_CreatedById",
                table: "BarcodeIdentities",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_DisabledById",
                table: "BarcodeIdentities",
                column: "DisabledById");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_ReplacedByBarcodeIdentityId",
                table: "BarcodeIdentities",
                column: "ReplacedByBarcodeIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_ReplacesBarcodeIdentityId",
                table: "BarcodeIdentities",
                column: "ReplacesBarcodeIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_RevokedById",
                table: "BarcodeIdentities",
                column: "RevokedById");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodeIdentities_TargetType_TargetId_Status",
                table: "BarcodeIdentities",
                columns: new[] { "TargetType", "TargetId", "Status" },
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BarcodeIdentities");
        }
    }
}
