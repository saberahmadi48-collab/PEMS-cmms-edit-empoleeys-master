using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrderChangeLogs",
                columns: table => new
                {
                    WorkOrderChangeLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecordRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    WorkOrderStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    WorkflowStage = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChangedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeforeValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSecurityEvent = table.Column<bool>(type: "bit", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderChangeLogs", x => x.WorkOrderChangeLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChangeLogs_IsSecurityEvent",
                table: "WorkOrderChangeLogs",
                column: "IsSecurityEvent");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChangeLogs_WorkOrderId_ChangedDate",
                table: "WorkOrderChangeLogs",
                columns: new[] { "WorkOrderId", "ChangedDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderChangeLogs");
        }
    }
}
