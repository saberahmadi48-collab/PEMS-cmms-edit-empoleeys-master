using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTypeCodeLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTypeCodes",
                columns: table => new
                {
                    DocumentTypeCodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportAliases = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypeCodes", x => x.DocumentTypeCodeId);
                });

            migrationBuilder.InsertData(
                table: "DocumentTypeCodes",
                columns: new[] { "DocumentTypeCodeId", "Code", "CreatedDate", "ImportAliases", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "MRQ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "MATERIAL REQUISITION", true, "Material Requisition" },
                    { 2, "SPC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "SPECIFICATION,SPEC", true, "Material / Equipment Specification" },
                    { 3, "POR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PURCHASE ORDER,PO", true, "Purchase Order" },
                    { 4, "JSS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Job Specification for Supply" },
                    { 5, "JSD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Job Specification for Design" },
                    { 6, "JSC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Job / Material Specification for Construction" },
                    { 7, "MTO", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "MATERIAL TAKE-OFF,MATERIAL TAKEOFF", true, "Material Take-off" },
                    { 8, "LST", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "LIST,SCHEDULE", true, "List / Schedule" },
                    { 9, "PFD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "UFD,PROCESS FLOW DIAGRAM", true, "PFD, UFD" },
                    { 10, "PID", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "P&ID,PIPING AND INSTRUMENTATION DIAGRAM", true, "P&ID, Utility & Instrument Diagram" },
                    { 11, "DWG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "DRAWING", true, "Drawing" },
                    { 12, "DIA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "DIAGRAM", true, "Diagram" },
                    { 13, "CLN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "CALCULATION NOTE,CALCULATION", true, "Calculation Note" },
                    { 14, "STD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "STANDARD", true, "Standard Drawing / Design" },
                    { 15, "PRC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PROCEDURE", true, "Procedure" },
                    { 16, "PLN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "PLAN", true, "Planning" },
                    { 17, "CHO", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "CHANGE ORDER", true, "Change Order" },
                    { 18, "DSH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "DATASHEET,DATA SHEET", true, "Data Sheet" },
                    { 19, "RPT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "REPORT", true, "Report" },
                    { 20, "CNT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "CONTRACT", true, "Contract" },
                    { 21, "ITP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "INSPECTION AND TEST PLAN,INSPECTION & TEST PLAN", true, "Inspection & Test Plan" },
                    { 22, "IRC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "INSPECTION RELEASE CERTIFICATE", true, "Inspection and Release Certificate" },
                    { 23, "RN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "RELEASE NOTE", true, "Release Note" },
                    { 24, "TBA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Technical Bid Analysis" },
                    { 25, "CBA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Commercial Bid Analysis" },
                    { 26, "TN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "TRANSMITTAL NOTE,TRANSMITTAL", true, "Transmittal Note" },
                    { 27, "NCR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "NON CONFORMANCE REPORT,NONCONFORMANCE", true, "Non Conformance Report" },
                    { 28, "MNL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "MANUAL", true, "Manual" },
                    { 29, "INX", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "INDEX", true, "Index" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTypeCodes");
        }
    }
}
