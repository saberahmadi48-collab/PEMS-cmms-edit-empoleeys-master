using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentFunctionalLocationAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentFunctionalLocationAssignmentBatches",
                columns: table => new
                {
                    EquipmentFunctionalLocationAssignmentBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentFunctionalLocationAssignmentBatches", x => x.EquipmentFunctionalLocationAssignmentBatchId);
                    table.ForeignKey(
                        name: "FK_EquipmentFunctionalLocationAssignmentBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentFunctionalLocationAssignmentRows",
                columns: table => new
                {
                    EquipmentFunctionalLocationAssignmentRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentFunctionalLocationAssignmentBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRowNumber = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    ResolvedFunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    PreviousFunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    ReplaceExistingAssignment = table.Column<bool>(type: "bit", nullable: false),
                    CommittedAssignment = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentFunctionalLocationAssignmentRows", x => x.EquipmentFunctionalLocationAssignmentRowId);
                    table.ForeignKey(
                        name: "FK_EquipmentFunctionalLocationAssignmentRows_EquipmentFunctionalLocationAssignmentBatches_EquipmentFunctionalLocationAssignment~",
                        column: x => x.EquipmentFunctionalLocationAssignmentBatchId,
                        principalTable: "EquipmentFunctionalLocationAssignmentBatches",
                        principalColumn: "EquipmentFunctionalLocationAssignmentBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentFunctionalLocationAssignmentBatches_ImportedById",
                table: "EquipmentFunctionalLocationAssignmentBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentFunctionalLocationAssignmentRows_EquipmentFunctionalLocationAssignmentBatchId",
                table: "EquipmentFunctionalLocationAssignmentRows",
                column: "EquipmentFunctionalLocationAssignmentBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentFunctionalLocationAssignmentRows");

            migrationBuilder.DropTable(
                name: "EquipmentFunctionalLocationAssignmentBatches");
        }
    }
}
