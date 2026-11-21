using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitIsolationPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IsolationPoints",
                columns: table => new
                {
                    IsolationPointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    EnergyType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredIsolationState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsolationMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsolatedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    IsolatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    VerifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeisolatedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DeisolatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsolationPoints", x => x.IsolationPointId);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "AssetId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Employees_DeisolatedByEmployeeId",
                        column: x => x.DeisolatedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Employees_IsolatedByEmployeeId",
                        column: x => x.IsolatedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Employees_VerifiedByEmployeeId",
                        column: x => x.VerifiedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_FunctionalLocations_FunctionalLocationId",
                        column: x => x.FunctionalLocationId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationPoints_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IsolationStatusHistories",
                columns: table => new
                {
                    IsolationStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsolationPointId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsolationStatusHistories", x => x.IsolationStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_IsolationStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IsolationStatusHistories_IsolationPoints_IsolationPointId",
                        column: x => x.IsolationPointId,
                        principalTable: "IsolationPoints",
                        principalColumn: "IsolationPointId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_AssetId",
                table: "IsolationPoints",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_DeisolatedByEmployeeId",
                table: "IsolationPoints",
                column: "DeisolatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_EquipmentId",
                table: "IsolationPoints",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_FunctionalLocationId",
                table: "IsolationPoints",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_IsolatedByEmployeeId",
                table: "IsolationPoints",
                column: "IsolatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_PermitId",
                table: "IsolationPoints",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationPoints_VerifiedByEmployeeId",
                table: "IsolationPoints",
                column: "VerifiedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationStatusHistories_ChangedById",
                table: "IsolationStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_IsolationStatusHistories_IsolationPointId",
                table: "IsolationStatusHistories",
                column: "IsolationPointId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IsolationStatusHistories");

            migrationBuilder.DropTable(
                name: "IsolationPoints");
        }
    }
}
