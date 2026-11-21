using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkRequestRiskFailureCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureEffect",
                table: "WorkRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureModeId",
                table: "WorkRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergency",
                table: "WorkRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHseRelated",
                table: "WorkRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PriorityRationale",
                table: "WorkRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReportedDowntimeHours",
                table: "WorkRequests",
                type: "decimal(9,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPriority",
                table: "WorkRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolutionDueDate",
                table: "WorkRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDueDate",
                table: "WorkRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "WorkRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskLikelihood",
                table: "WorkRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                table: "WorkRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskSeverity",
                table: "WorkRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureEffect",
                table: "WorkOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHseRelated",
                table: "WorkOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_FailureModeId",
                table: "WorkRequests",
                column: "FailureModeId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkRequests_FailureModes_FailureModeId",
                table: "WorkRequests",
                column: "FailureModeId",
                principalTable: "FailureModes",
                principalColumn: "FailureModeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkRequests_FailureModes_FailureModeId",
                table: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkRequests_FailureModeId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "FailureEffect",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "FailureModeId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "IsEmergency",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "IsHseRelated",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "PriorityRationale",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "ReportedDowntimeHours",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RequestedPriority",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "ResolutionDueDate",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "ResponseDueDate",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RiskLikelihood",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RiskSeverity",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "FailureEffect",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "IsHseRelated",
                table: "WorkOrders");
        }
    }
}
