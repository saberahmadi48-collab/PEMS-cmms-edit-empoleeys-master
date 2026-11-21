using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QualificationCertificationDiscipline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisciplineId",
                table: "Qualifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisciplineId",
                table: "Certifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Qualifications_DisciplineId",
                table: "Qualifications",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Certifications_DisciplineId",
                table: "Certifications",
                column: "DisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Certifications_Disciplines_DisciplineId",
                table: "Certifications",
                column: "DisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Qualifications_Disciplines_DisciplineId",
                table: "Qualifications",
                column: "DisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Certifications_Disciplines_DisciplineId",
                table: "Certifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Qualifications_Disciplines_DisciplineId",
                table: "Qualifications");

            migrationBuilder.DropIndex(
                name: "IX_Qualifications_DisciplineId",
                table: "Qualifications");

            migrationBuilder.DropIndex(
                name: "IX_Certifications_DisciplineId",
                table: "Certifications");

            migrationBuilder.DropColumn(
                name: "DisciplineId",
                table: "Qualifications");

            migrationBuilder.DropColumn(
                name: "DisciplineId",
                table: "Certifications");
        }
    }
}
