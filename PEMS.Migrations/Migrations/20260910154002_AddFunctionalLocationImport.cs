using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFunctionalLocationImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FunctionalLocationImportBatches",
                columns: table => new
                {
                    FunctionalLocationImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalLocationImportBatches", x => x.FunctionalLocationImportBatchId);
                    table.ForeignKey(
                        name: "FK_FunctionalLocationImportBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                });

            migrationBuilder.CreateTable(
                name: "FunctionalLocationImportRows",
                columns: table => new
                {
                    FunctionalLocationImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FunctionalLocationImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsUpdate = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedFunctionalLocationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalLocationImportRows", x => x.FunctionalLocationImportRowId);
                    table.ForeignKey(
                        name: "FK_FunctionalLocationImportRows_FunctionalLocationImportBatches_FunctionalLocationImportBatchId",
                        column: x => x.FunctionalLocationImportBatchId,
                        principalTable: "FunctionalLocationImportBatches",
                        principalColumn: "FunctionalLocationImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalLocationImportBatches_ImportedById",
                table: "FunctionalLocationImportBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalLocationImportRows_FunctionalLocationImportBatchId",
                table: "FunctionalLocationImportRows",
                column: "FunctionalLocationImportBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FunctionalLocationImportRows");

            migrationBuilder.DropTable(
                name: "FunctionalLocationImportBatches");
        }
    }
}
