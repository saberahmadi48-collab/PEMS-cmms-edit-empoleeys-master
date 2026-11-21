using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_ConditionReadingImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConditionReadingImportBatches",
                columns: table => new
                {
                    ConditionReadingImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionReadingImportBatches", x => x.ConditionReadingImportBatchId);
                    table.ForeignKey(
                        name: "FK_ConditionReadingImportBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConditionReadingImportRows",
                columns: table => new
                {
                    ConditionReadingImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConditionReadingImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedConditionMonitoringPointId = table.Column<int>(type: "int", nullable: true),
                    DuplicateOfConditionReadingId = table.Column<int>(type: "int", nullable: true),
                    CommittedConditionReadingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionReadingImportRows", x => x.ConditionReadingImportRowId);
                    table.ForeignKey(
                        name: "FK_ConditionReadingImportRows_ConditionReadingImportBatches_ConditionReadingImportBatchId",
                        column: x => x.ConditionReadingImportBatchId,
                        principalTable: "ConditionReadingImportBatches",
                        principalColumn: "ConditionReadingImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConditionReadingImportBatches_ImportedById",
                table: "ConditionReadingImportBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionReadingImportRows_ConditionReadingImportBatchId",
                table: "ConditionReadingImportRows",
                column: "ConditionReadingImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConditionReadingImportRows");

            migrationBuilder.DropTable(
                name: "ConditionReadingImportBatches");
        }
    }
}
