using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreEmployeePersonnelTypeAndPermit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonnelType",
                table: "Employees",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Internal");

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Permits",
                columns: table => new
                {
                    PermitId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PermitType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    RequestedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    PermitIssuerEmployeeId = table.Column<int>(type: "int", nullable: true),
                    PermitReceiverEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permits", x => x.PermitId);
                    table.ForeignKey(
                        name: "FK_Permits_Employees_PermitIssuerEmployeeId",
                        column: x => x.PermitIssuerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Employees_PermitReceiverEmployeeId",
                        column: x => x.PermitReceiverEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Employees_RequestedByEmployeeId",
                        column: x => x.RequestedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_FunctionalLocations_FunctionalLocationId",
                        column: x => x.FunctionalLocationId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleModuleActions",
                columns: table => new
                {
                    RoleModuleActionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleModuleActions", x => x.RoleModuleActionId);
                });

            migrationBuilder.CreateTable(
                name: "PermitStatusHistories",
                columns: table => new
                {
                    PermitStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitStatusHistories", x => x.PermitStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_PermitStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitStatusHistories_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_VendorId",
                table: "Employees",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_EquipmentId",
                table: "Permits",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_FunctionalLocationId",
                table: "Permits",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_PermitIssuerEmployeeId",
                table: "Permits",
                column: "PermitIssuerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_PermitReceiverEmployeeId",
                table: "Permits",
                column: "PermitReceiverEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_RequestedByEmployeeId",
                table: "Permits",
                column: "RequestedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_WorkOrderId",
                table: "Permits",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitStatusHistories_ChangedById",
                table: "PermitStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_PermitStatusHistories_PermitId",
                table: "PermitStatusHistories",
                column: "PermitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Vendors_VendorId",
                table: "Employees",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "VendorId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Vendors_VendorId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "PermitStatusHistories");

            migrationBuilder.DropTable(
                name: "RoleModuleActions");

            migrationBuilder.DropTable(
                name: "Permits");

            migrationBuilder.DropIndex(
                name: "IX_Employees_VendorId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PersonnelType",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Employees");
        }
    }
}
