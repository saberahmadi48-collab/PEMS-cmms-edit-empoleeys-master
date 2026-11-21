using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIrnDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AcceptedQuantity",
                table: "IrnReceiptLines",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HoldQuantity",
                table: "IrnReceiptLines",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RejectedQuantity",
                table: "IrnReceiptLines",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IrnDispositionRecords",
                columns: table => new
                {
                    IrnDispositionRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IrnReceiptId = table.Column<int>(type: "int", nullable: false),
                    DispositionedById = table.Column<int>(type: "int", nullable: false),
                    DispositionedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OverallResult = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NonConformanceReportId = table.Column<int>(type: "int", nullable: true),
                    WorkflowOutcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrnDispositionRecords", x => x.IrnDispositionRecordId);
                    table.ForeignKey(
                        name: "FK_IrnDispositionRecords_Employees_DispositionedById",
                        column: x => x.DispositionedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnDispositionRecords_IrnReceipts_IrnReceiptId",
                        column: x => x.IrnReceiptId,
                        principalTable: "IrnReceipts",
                        principalColumn: "IrnReceiptId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IrnDispositionRecords_NonConformanceReports_NonConformanceReportId",
                        column: x => x.NonConformanceReportId,
                        principalTable: "NonConformanceReports",
                        principalColumn: "NonConformanceReportId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IrnDispositionRecords_DispositionedById",
                table: "IrnDispositionRecords",
                column: "DispositionedById");

            migrationBuilder.CreateIndex(
                name: "IX_IrnDispositionRecords_IrnReceiptId",
                table: "IrnDispositionRecords",
                column: "IrnReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnDispositionRecords_NonConformanceReportId",
                table: "IrnDispositionRecords",
                column: "NonConformanceReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IrnDispositionRecords");

            migrationBuilder.DropColumn(
                name: "AcceptedQuantity",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "HoldQuantity",
                table: "IrnReceiptLines");

            migrationBuilder.DropColumn(
                name: "RejectedQuantity",
                table: "IrnReceiptLines");
        }
    }
}
