using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitGasTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GasParameterLimits",
                columns: table => new
                {
                    GasParameterLimitId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Parameter = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PermitType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasParameterLimits", x => x.GasParameterLimitId);
                });

            migrationBuilder.CreateTable(
                name: "GasTests",
                columns: table => new
                {
                    GasTestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TesterEmployeeId = table.Column<int>(type: "int", nullable: false),
                    InstrumentTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InstrumentCalibrationDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasTests", x => x.GasTestId);
                    table.ForeignKey(
                        name: "FK_GasTests_Employees_TesterEmployeeId",
                        column: x => x.TesterEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GasTests_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GasTests_FunctionalLocations_FunctionalLocationId",
                        column: x => x.FunctionalLocationId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GasTests_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GasTestValidityRules",
                columns: table => new
                {
                    GasTestValidityRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidityMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasTestValidityRules", x => x.GasTestValidityRuleId);
                });

            migrationBuilder.CreateTable(
                name: "GasReadings",
                columns: table => new
                {
                    GasReadingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GasTestId = table.Column<int>(type: "int", nullable: false),
                    Parameter = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasReadings", x => x.GasReadingId);
                    table.ForeignKey(
                        name: "FK_GasReadings_GasTests_GasTestId",
                        column: x => x.GasTestId,
                        principalTable: "GasTests",
                        principalColumn: "GasTestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GasTestStatusHistories",
                columns: table => new
                {
                    GasTestStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GasTestId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GasTestStatusHistories", x => x.GasTestStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_GasTestStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GasTestStatusHistories_GasTests_GasTestId",
                        column: x => x.GasTestId,
                        principalTable: "GasTests",
                        principalColumn: "GasTestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GasReadings_GasTestId",
                table: "GasReadings",
                column: "GasTestId");

            migrationBuilder.CreateIndex(
                name: "IX_GasTests_EquipmentId",
                table: "GasTests",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_GasTests_FunctionalLocationId",
                table: "GasTests",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_GasTests_PermitId",
                table: "GasTests",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_GasTests_TesterEmployeeId",
                table: "GasTests",
                column: "TesterEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_GasTestStatusHistories_ChangedById",
                table: "GasTestStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_GasTestStatusHistories_GasTestId",
                table: "GasTestStatusHistories",
                column: "GasTestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GasParameterLimits");

            migrationBuilder.DropTable(
                name: "GasReadings");

            migrationBuilder.DropTable(
                name: "GasTestStatusHistories");

            migrationBuilder.DropTable(
                name: "GasTestValidityRules");

            migrationBuilder.DropTable(
                name: "GasTests");
        }
    }
}
