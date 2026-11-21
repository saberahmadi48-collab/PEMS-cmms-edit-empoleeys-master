using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringNumberingAndFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanFreeze",
                table: "RolePermissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUnfreeze",
                table: "RolePermissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNo",
                table: "Documents",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "FreezeReason",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrozenById",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnfreezeReason",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnfrozenById",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnfrozenDate",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentNo",
                table: "Documents",
                column: "DocumentNo",
                unique: true,
                filter: "[DocumentNo] IS NOT NULL AND [DocumentNo] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FrozenById",
                table: "Documents",
                column: "FrozenById");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UnfrozenById",
                table: "Documents",
                column: "UnfrozenById");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Employees_FrozenById",
                table: "Documents",
                column: "FrozenById",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Employees_UnfrozenById",
                table: "Documents",
                column: "UnfrozenById",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Employees_FrozenById",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Employees_UnfrozenById",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DocumentNo",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_FrozenById",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UnfrozenById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "CanFreeze",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "CanUnfreeze",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "FreezeReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FrozenById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UnfreezeReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UnfrozenById",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UnfrozenDate",
                table: "Documents");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNo",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
