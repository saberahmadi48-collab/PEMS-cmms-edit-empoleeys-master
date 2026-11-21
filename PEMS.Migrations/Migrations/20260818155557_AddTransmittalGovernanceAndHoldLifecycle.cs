using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransmittalGovernanceAndHoldLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancelledById",
                table: "Transmittals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledDate",
                table: "Transmittals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledReason",
                table: "Transmittals",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDueDate",
                table: "Transmittals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseNote",
                table: "Transmittals",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseReceivedDate",
                table: "Transmittals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResponseRequired",
                table: "Transmittals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Transmittals",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentDate",
                table: "Transmittals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersedesTransmittalId",
                table: "Transmittals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanHold",
                table: "RolePermissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanReleaseHold",
                table: "RolePermissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnHold",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MarksRevisionRequired",
                table: "DocumentComments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TriggeredRevisionId",
                table: "DocumentComments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentHolds",
                columns: table => new
                {
                    DocumentHoldId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HoldDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HoldByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ResponsibleDisciplineId = table.Column<int>(type: "int", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleaseByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentHolds", x => x.DocumentHoldId);
                    table.ForeignKey(
                        name: "FK_DocumentHolds_Disciplines_ResponsibleDisciplineId",
                        column: x => x.ResponsibleDisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentHolds_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentHolds_Employees_HoldByEmployeeId",
                        column: x => x.HoldByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentHolds_Employees_ReleaseByEmployeeId",
                        column: x => x.ReleaseByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransmittalRecipients",
                columns: table => new
                {
                    TransmittalRecipientId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransmittalId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransmittalRecipients", x => x.TransmittalRecipientId);
                    table.ForeignKey(
                        name: "FK_TransmittalRecipients_Transmittals_TransmittalId",
                        column: x => x.TransmittalId,
                        principalTable: "Transmittals",
                        principalColumn: "TransmittalId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_CancelledById",
                table: "Transmittals",
                column: "CancelledById");

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_SupersedesTransmittalId",
                table: "Transmittals",
                column: "SupersedesTransmittalId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_TriggeredRevisionId",
                table: "DocumentComments",
                column: "TriggeredRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentHolds_DocumentId",
                table: "DocumentHolds",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentHolds_HoldByEmployeeId",
                table: "DocumentHolds",
                column: "HoldByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentHolds_ReleaseByEmployeeId",
                table: "DocumentHolds",
                column: "ReleaseByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentHolds_ResponsibleDisciplineId",
                table: "DocumentHolds",
                column: "ResponsibleDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalRecipients_TransmittalId",
                table: "TransmittalRecipients",
                column: "TransmittalId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentComments_DocumentRevisions_TriggeredRevisionId",
                table: "DocumentComments",
                column: "TriggeredRevisionId",
                principalTable: "DocumentRevisions",
                principalColumn: "RevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transmittals_Employees_CancelledById",
                table: "Transmittals",
                column: "CancelledById",
                principalTable: "Employees",
                principalColumn: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transmittals_Transmittals_SupersedesTransmittalId",
                table: "Transmittals",
                column: "SupersedesTransmittalId",
                principalTable: "Transmittals",
                principalColumn: "TransmittalId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentComments_DocumentRevisions_TriggeredRevisionId",
                table: "DocumentComments");

            migrationBuilder.DropForeignKey(
                name: "FK_Transmittals_Employees_CancelledById",
                table: "Transmittals");

            migrationBuilder.DropForeignKey(
                name: "FK_Transmittals_Transmittals_SupersedesTransmittalId",
                table: "Transmittals");

            migrationBuilder.DropTable(
                name: "DocumentHolds");

            migrationBuilder.DropTable(
                name: "TransmittalRecipients");

            migrationBuilder.DropIndex(
                name: "IX_Transmittals_CancelledById",
                table: "Transmittals");

            migrationBuilder.DropIndex(
                name: "IX_Transmittals_SupersedesTransmittalId",
                table: "Transmittals");

            migrationBuilder.DropIndex(
                name: "IX_DocumentComments_TriggeredRevisionId",
                table: "DocumentComments");

            migrationBuilder.DropColumn(
                name: "CancelledById",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "CancelledDate",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "CancelledReason",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "ResponseDueDate",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "ResponseNote",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "ResponseReceivedDate",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "ResponseRequired",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "SentDate",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "SupersedesTransmittalId",
                table: "Transmittals");

            migrationBuilder.DropColumn(
                name: "CanHold",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "CanReleaseHold",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "IsOnHold",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "MarksRevisionRequired",
                table: "DocumentComments");

            migrationBuilder.DropColumn(
                name: "TriggeredRevisionId",
                table: "DocumentComments");
        }
    }
}
