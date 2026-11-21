using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationPositionHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeAssignments_EmployeeId",
                table: "EmployeeAssignments");

            migrationBuilder.AddColumn<int>(
                name: "ParentPositionId",
                table: "Positions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PositionCode",
                table: "Positions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoutingBlockReason",
                table: "PartCatalogueRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignmentType",
                table: "EmployeeAssignments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Primary");

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "EmployeeAssignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_ParentPositionId",
                table: "Positions",
                column: "ParentPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PositionCode",
                table: "Positions",
                column: "PositionCode",
                unique: true,
                filter: "[PositionCode] IS NOT NULL");

            // ---- Backfill قطعی (بدون حدس): تخصیص فعالِ «تنها تخصیص فعال» یک کارمند، سمت اصلی او می‌شود؛ کارمندی که چند تخصیص فعال دارد
            // هیچ‌کدام را اصلی نمی‌گیرد (ابهام) و در Diagnostics «EMPLOYEE_NO_PRIMARY_POSITION» می‌شود تا مدیر سازمان تعیین کند.
            // هیچ خط گزارش‌دهی/مدیری ساخته یا استنباط نمی‌شود (ParentPositionId همه NULL است).
            migrationBuilder.Sql(@"
UPDATE a SET a.IsPrimary = 1
FROM EmployeeAssignments a
WHERE a.IsActive = 1 AND (SELECT COUNT(*) FROM EmployeeAssignments x WHERE x.EmployeeId = a.EmployeeId AND x.IsActive = 1) = 1;");
            migrationBuilder.Sql(@"UPDATE EmployeeAssignments SET AssignmentType = 'Secondary' WHERE IsPrimary = 0 AND IsActive = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssignments_EmployeeId_ActivePrimary",
                table: "EmployeeAssignments",
                column: "EmployeeId",
                unique: true,
                filter: "[IsActive] = 1 AND [IsPrimary] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Positions_Positions_ParentPositionId",
                table: "Positions",
                column: "ParentPositionId",
                principalTable: "Positions",
                principalColumn: "PositionId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Positions_Positions_ParentPositionId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_ParentPositionId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_PositionCode",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeAssignments_EmployeeId_ActivePrimary",
                table: "EmployeeAssignments");

            migrationBuilder.DropColumn(
                name: "ParentPositionId",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "PositionCode",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "RoutingBlockReason",
                table: "PartCatalogueRequests");

            migrationBuilder.DropColumn(
                name: "AssignmentType",
                table: "EmployeeAssignments");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "EmployeeAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAssignments_EmployeeId",
                table: "EmployeeAssignments",
                column: "EmployeeId");
        }
    }
}
