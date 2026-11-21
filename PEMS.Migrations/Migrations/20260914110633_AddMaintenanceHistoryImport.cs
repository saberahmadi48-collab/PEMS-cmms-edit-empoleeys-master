using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceHistoryImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceHistoryImportBatches",
                columns: table => new
                {
                    MaintenanceHistoryImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceHistoryImportBatches", x => x.MaintenanceHistoryImportBatchId);
                    table.ForeignKey(
                        name: "FK_MaintenanceHistoryImportBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceHistoryImportRows",
                columns: table => new
                {
                    MaintenanceHistoryImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceHistoryImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    ResolvedWorkOrderId = table.Column<int>(type: "int", nullable: true),
                    DuplicateOfMaintenanceHistoryId = table.Column<int>(type: "int", nullable: true),
                    CommittedMaintenanceHistoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceHistoryImportRows", x => x.MaintenanceHistoryImportRowId);
                    table.ForeignKey(
                        name: "FK_MaintenanceHistoryImportRows_MaintenanceHistoryImportBatches_MaintenanceHistoryImportBatchId",
                        column: x => x.MaintenanceHistoryImportBatchId,
                        principalTable: "MaintenanceHistoryImportBatches",
                        principalColumn: "MaintenanceHistoryImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceHistoryImportBatches_ImportedById",
                table: "MaintenanceHistoryImportBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceHistoryImportRows_MaintenanceHistoryImportBatchId",
                table: "MaintenanceHistoryImportRows",
                column: "MaintenanceHistoryImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceHistoryImportRows");

            migrationBuilder.DropTable(
                name: "MaintenanceHistoryImportBatches");
        }
    }
}
