using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalityAssessmentWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CriticalityAssessments",
                columns: table => new
                {
                    CriticalityAssessmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    FrameworkVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AssessmentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByAssessmentId = table.Column<int>(type: "int", nullable: true),
                    SafetyScore = table.Column<int>(type: "int", nullable: true),
                    ProductionScore = table.Column<int>(type: "int", nullable: true),
                    EnvironmentScore = table.Column<int>(type: "int", nullable: true),
                    Rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReasonForChange = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalityAssessments", x => x.CriticalityAssessmentId);
                    table.ForeignKey(
                        name: "FK_CriticalityAssessments_CriticalityAssessments_SupersededByAssessmentId",
                        column: x => x.SupersededByAssessmentId,
                        principalTable: "CriticalityAssessments",
                        principalColumn: "CriticalityAssessmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CriticalityAssessments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CriticalityAssessmentEvidences",
                columns: table => new
                {
                    CriticalityAssessmentEvidenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CriticalityAssessmentId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceIdentifier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalityAssessmentEvidences", x => x.CriticalityAssessmentEvidenceId);
                    table.ForeignKey(
                        name: "FK_CriticalityAssessmentEvidences_CriticalityAssessments_CriticalityAssessmentId",
                        column: x => x.CriticalityAssessmentId,
                        principalTable: "CriticalityAssessments",
                        principalColumn: "CriticalityAssessmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalityAssessmentEvidences_CriticalityAssessmentId",
                table: "CriticalityAssessmentEvidences",
                column: "CriticalityAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CriticalityAssessments_EquipmentId_Status",
                table: "CriticalityAssessments",
                columns: new[] { "EquipmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalityAssessments_SupersededByAssessmentId",
                table: "CriticalityAssessments",
                column: "SupersededByAssessmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriticalityAssessmentEvidences");

            migrationBuilder.DropTable(
                name: "CriticalityAssessments");
        }
    }
}
