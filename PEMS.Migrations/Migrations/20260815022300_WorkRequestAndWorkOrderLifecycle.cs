using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkRequestAndWorkOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceWorkRequestId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkRequests",
                columns: table => new
                {
                    WorkRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    ReportedCondition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedDisciplineId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkedWorkOrderId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRequests", x => x.WorkRequestId);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Disciplines_SuggestedDisciplineId",
                        column: x => x.SuggestedDisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Employees_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_FunctionalLocations_FunctionalLocationId",
                        column: x => x.FunctionalLocationId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkRequests_WorkOrders_LinkedWorkOrderId",
                        column: x => x.LinkedWorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkRequestStatusHistories",
                columns: table => new
                {
                    WorkRequestStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkRequestId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRequestStatusHistories", x => x.WorkRequestStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_WorkRequestStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_WorkRequestStatusHistories_WorkRequests_WorkRequestId",
                        column: x => x.WorkRequestId,
                        principalTable: "WorkRequests",
                        principalColumn: "WorkRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SourceWorkRequestId",
                table: "WorkOrders",
                column: "SourceWorkRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_ApprovedById",
                table: "WorkRequests",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_AssetId",
                table: "WorkRequests",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_EquipmentId",
                table: "WorkRequests",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_FunctionalLocationId",
                table: "WorkRequests",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_LinkedWorkOrderId",
                table: "WorkRequests",
                column: "LinkedWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_RequestedById",
                table: "WorkRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_ReviewedById",
                table: "WorkRequests",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_SuggestedDisciplineId",
                table: "WorkRequests",
                column: "SuggestedDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequestStatusHistories_ChangedById",
                table: "WorkRequestStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequestStatusHistories_WorkRequestId",
                table: "WorkRequestStatusHistories",
                column: "WorkRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_WorkRequests_SourceWorkRequestId",
                table: "WorkOrders",
                column: "SourceWorkRequestId",
                principalTable: "WorkRequests",
                principalColumn: "WorkRequestId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_WorkRequests_SourceWorkRequestId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "WorkRequestStatusHistories");

            migrationBuilder.DropTable(
                name: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SourceWorkRequestId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceWorkRequestId",
                table: "WorkOrders");
        }
    }
}
