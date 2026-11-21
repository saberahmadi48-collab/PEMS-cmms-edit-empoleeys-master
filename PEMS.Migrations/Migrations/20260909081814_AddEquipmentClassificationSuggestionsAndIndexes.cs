using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentClassificationSuggestionsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ClassificationSource",
                table: "Equipments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClassificationConfidence",
                table: "Equipments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentClassificationSuggestions",
                columns: table => new
                {
                    EquipmentClassificationSuggestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    SuggestedEquipmentClassId = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentClassificationSuggestions", x => x.EquipmentClassificationSuggestionId);
                    table.ForeignKey(
                        name: "FK_EquipmentClassificationSuggestions_EquipmentClasses_SuggestedEquipmentClassId",
                        column: x => x.SuggestedEquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentClassificationSuggestions_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_ClassificationConfidence",
                table: "Equipments",
                column: "ClassificationConfidence");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_ClassificationSource",
                table: "Equipments",
                column: "ClassificationSource");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_IsActive_EquipmentClassId",
                table: "Equipments",
                columns: new[] { "IsActive", "EquipmentClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentClassificationSuggestions_EquipmentId_Status",
                table: "EquipmentClassificationSuggestions",
                columns: new[] { "EquipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentClassificationSuggestions_Status",
                table: "EquipmentClassificationSuggestions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentClassificationSuggestions_SuggestedEquipmentClassId",
                table: "EquipmentClassificationSuggestions",
                column: "SuggestedEquipmentClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentClassificationSuggestions");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_ClassificationConfidence",
                table: "Equipments");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_ClassificationSource",
                table: "Equipments");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_IsActive_EquipmentClassId",
                table: "Equipments");

            migrationBuilder.AlterColumn<string>(
                name: "ClassificationSource",
                table: "Equipments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClassificationConfidence",
                table: "Equipments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
