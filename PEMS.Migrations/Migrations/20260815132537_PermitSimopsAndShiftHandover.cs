using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitSimopsAndShiftHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftHandovers",
                columns: table => new
                {
                    ShiftHandoverId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutgoingEmployeeId = table.Column<int>(type: "int", nullable: false),
                    IncomingEmployeeId = table.Column<int>(type: "int", nullable: false),
                    ShiftLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HandoverDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftHandovers", x => x.ShiftHandoverId);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Employees_IncomingEmployeeId",
                        column: x => x.IncomingEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftHandovers_Employees_OutgoingEmployeeId",
                        column: x => x.OutgoingEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimopsConflicts",
                columns: table => new
                {
                    SimopsConflictId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitAId = table.Column<int>(type: "int", nullable: false),
                    PermitBId = table.Column<int>(type: "int", nullable: false),
                    ConflictBasis = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RaisedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    AssessedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AssessedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ResolvedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AcceptedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AcceptedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptanceJustification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimopsConflicts", x => x.SimopsConflictId);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Employees_AcceptedByEmployeeId",
                        column: x => x.AcceptedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Employees_AssessedByEmployeeId",
                        column: x => x.AssessedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Employees_RaisedByEmployeeId",
                        column: x => x.RaisedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Employees_ResolvedByEmployeeId",
                        column: x => x.ResolvedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Permits_PermitAId",
                        column: x => x.PermitAId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflicts_Permits_PermitBId",
                        column: x => x.PermitBId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimopsInteractionRules",
                columns: table => new
                {
                    SimopsInteractionRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitTypeA = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PermitTypeB = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimopsInteractionRules", x => x.SimopsInteractionRuleId);
                });

            migrationBuilder.CreateTable(
                name: "ShiftHandoverItems",
                columns: table => new
                {
                    ShiftHandoverItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftHandoverId = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftHandoverItems", x => x.ShiftHandoverItemId);
                    table.ForeignKey(
                        name: "FK_ShiftHandoverItems_ShiftHandovers_ShiftHandoverId",
                        column: x => x.ShiftHandoverId,
                        principalTable: "ShiftHandovers",
                        principalColumn: "ShiftHandoverId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftHandoverStatusHistories",
                columns: table => new
                {
                    ShiftHandoverStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftHandoverId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftHandoverStatusHistories", x => x.ShiftHandoverStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_ShiftHandoverStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftHandoverStatusHistories_ShiftHandovers_ShiftHandoverId",
                        column: x => x.ShiftHandoverId,
                        principalTable: "ShiftHandovers",
                        principalColumn: "ShiftHandoverId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SimopsConflictStatusHistories",
                columns: table => new
                {
                    SimopsConflictStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SimopsConflictId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimopsConflictStatusHistories", x => x.SimopsConflictStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_SimopsConflictStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimopsConflictStatusHistories_SimopsConflicts_SimopsConflictId",
                        column: x => x.SimopsConflictId,
                        principalTable: "SimopsConflicts",
                        principalColumn: "SimopsConflictId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandoverItems_ShiftHandoverId",
                table: "ShiftHandoverItems",
                column: "ShiftHandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_IncomingEmployeeId",
                table: "ShiftHandovers",
                column: "IncomingEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandovers_OutgoingEmployeeId",
                table: "ShiftHandovers",
                column: "OutgoingEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandoverStatusHistories_ChangedById",
                table: "ShiftHandoverStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftHandoverStatusHistories_ShiftHandoverId",
                table: "ShiftHandoverStatusHistories",
                column: "ShiftHandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_AcceptedByEmployeeId",
                table: "SimopsConflicts",
                column: "AcceptedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_AssessedByEmployeeId",
                table: "SimopsConflicts",
                column: "AssessedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_PermitAId",
                table: "SimopsConflicts",
                column: "PermitAId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_PermitBId",
                table: "SimopsConflicts",
                column: "PermitBId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_RaisedByEmployeeId",
                table: "SimopsConflicts",
                column: "RaisedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflicts_ResolvedByEmployeeId",
                table: "SimopsConflicts",
                column: "ResolvedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflictStatusHistories_ChangedById",
                table: "SimopsConflictStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_SimopsConflictStatusHistories_SimopsConflictId",
                table: "SimopsConflictStatusHistories",
                column: "SimopsConflictId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftHandoverItems");

            migrationBuilder.DropTable(
                name: "ShiftHandoverStatusHistories");

            migrationBuilder.DropTable(
                name: "SimopsConflictStatusHistories");

            migrationBuilder.DropTable(
                name: "SimopsInteractionRules");

            migrationBuilder.DropTable(
                name: "ShiftHandovers");

            migrationBuilder.DropTable(
                name: "SimopsConflicts");
        }
    }
}
