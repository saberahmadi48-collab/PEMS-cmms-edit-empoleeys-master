using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LetterInternalRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovalRequired",
                table: "Letters",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedDate",
                table: "Letters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Letters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "Letters",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedDepartmentId",
                table: "Letters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SenderDepartmentId",
                table: "Letters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SenderEmployeeId",
                table: "Letters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderTitle",
                table: "Letters",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LetterApprovalSteps",
                columns: table => new
                {
                    LetterApprovalStepId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverUserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterApprovalSteps", x => x.LetterApprovalStepId);
                    table.ForeignKey(
                        name: "FK_LetterApprovalSteps_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterNumberSequences",
                columns: table => new
                {
                    LetterNumberSequenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterNumberSequences", x => x.LetterNumberSequenceId);
                });

            migrationBuilder.CreateTable(
                name: "LetterVersions",
                columns: table => new
                {
                    LetterVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SavedByUserId = table.Column<int>(type: "int", nullable: true),
                    SavedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterVersions", x => x.LetterVersionId);
                    table.ForeignKey(
                        name: "FK_LetterVersions_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterViewLogs",
                columns: table => new
                {
                    LetterViewLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstViewedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterViewLogs", x => x.LetterViewLogId);
                    table.ForeignKey(
                        name: "FK_LetterViewLogs_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Letters_RegisteredById",
                table: "Letters",
                column: "RegisteredById");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_SenderEmployeeId",
                table: "Letters",
                column: "SenderEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_Status_IsActive",
                table: "Letters",
                columns: new[] { "Status", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterApprovalSteps_ApproverUserId_Status",
                table: "LetterApprovalSteps",
                columns: new[] { "ApproverUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterApprovalSteps_LetterId_StepOrder",
                table: "LetterApprovalSteps",
                columns: new[] { "LetterId", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterNumberSequences_Scope_Year",
                table: "LetterNumberSequences",
                columns: new[] { "Scope", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterVersions_LetterId_VersionNo",
                table: "LetterVersions",
                columns: new[] { "LetterId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterViewLogs_LetterId_UserId",
                table: "LetterViewLogs",
                columns: new[] { "LetterId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LetterApprovalSteps");

            migrationBuilder.DropTable(
                name: "LetterNumberSequences");

            migrationBuilder.DropTable(
                name: "LetterVersions");

            migrationBuilder.DropTable(
                name: "LetterViewLogs");

            migrationBuilder.DropIndex(
                name: "IX_Letters_RegisteredById",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_SenderEmployeeId",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_Status_IsActive",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ApprovalRequired",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ArchivedDate",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "RelatedDepartmentId",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "SenderDepartmentId",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "SenderEmployeeId",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "SenderTitle",
                table: "Letters");
        }
    }
}
