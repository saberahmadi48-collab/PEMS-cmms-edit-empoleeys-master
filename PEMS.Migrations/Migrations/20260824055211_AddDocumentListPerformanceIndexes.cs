using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentListPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Documents_IsActive_DisciplineId_CreatedDate",
                table: "Documents",
                columns: new[] { "IsActive", "DisciplineId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IsActive_ProjectId_CreatedDate",
                table: "Documents",
                columns: new[] { "IsActive", "ProjectId", "CreatedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_IsActive_DisciplineId_CreatedDate",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_IsActive_ProjectId_CreatedDate",
                table: "Documents");
        }
    }
}
