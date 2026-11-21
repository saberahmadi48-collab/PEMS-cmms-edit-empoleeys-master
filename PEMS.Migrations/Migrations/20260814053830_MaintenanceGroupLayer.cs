using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceGroupLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Disciplines_DisciplineId",
                table: "Teams");

            migrationBuilder.RenameColumn(
                name: "DisciplineId",
                table: "Teams",
                newName: "MaintenanceGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_DisciplineId",
                table: "Teams",
                newName: "IX_Teams_MaintenanceGroupId");

            migrationBuilder.CreateTable(
                name: "MaintenanceGroups",
                columns: table => new
                {
                    MaintenanceGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceGroups", x => x.MaintenanceGroupId);
                    table.ForeignKey(
                        name: "FK_MaintenanceGroups_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceGroups_Code",
                table: "MaintenanceGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceGroups_DisciplineId",
                table: "MaintenanceGroups",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceGroups_Name",
                table: "MaintenanceGroups",
                column: "Name",
                unique: true);

            // Data fix-up: the rename above carried the old Team.DisciplineId value straight into
            // MaintenanceGroupId, but MaintenanceGroups is a brand-new empty table - that value no
            // longer points at a real row. Create one real MaintenanceGroup per Discipline that an
            // existing Team currently references, then repoint those Teams at it, before the FK
            // constraint below is added (which would otherwise fail validation against the stale IDs).
            migrationBuilder.Sql(@"
                INSERT INTO MaintenanceGroups (Code, Name, DisciplineId, Description, IsActive, CreatedDate)
                SELECT 'MIGRATED-' + d.Code, N'General ' + d.Name + N' Maintenance', d.DisciplineId,
                       N'به‌صورت خودکار هنگام مهاجرت Team از Discipline به MaintenanceGroup ساخته شد.', 1, GETDATE()
                FROM Disciplines d
                WHERE d.DisciplineId IN (SELECT DISTINCT MaintenanceGroupId FROM Teams);

                UPDATE t
                SET t.MaintenanceGroupId = mg.MaintenanceGroupId
                FROM Teams t
                INNER JOIN MaintenanceGroups mg ON mg.DisciplineId = t.MaintenanceGroupId AND mg.Code LIKE 'MIGRATED-%';
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_MaintenanceGroups_MaintenanceGroupId",
                table: "Teams",
                column: "MaintenanceGroupId",
                principalTable: "MaintenanceGroups",
                principalColumn: "MaintenanceGroupId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_MaintenanceGroups_MaintenanceGroupId",
                table: "Teams");

            migrationBuilder.DropTable(
                name: "MaintenanceGroups");

            migrationBuilder.RenameColumn(
                name: "MaintenanceGroupId",
                table: "Teams",
                newName: "DisciplineId");

            migrationBuilder.RenameIndex(
                name: "IX_Teams_MaintenanceGroupId",
                table: "Teams",
                newName: "IX_Teams_DisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Disciplines_DisciplineId",
                table: "Teams",
                column: "DisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
