using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementClarificationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ProcurementCommunications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                table: "ProcurementCommunications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProcurementCommunications",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProcurementCommunications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenderId",
                table: "ProcurementCommunications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcurementCommunicationResponses",
                columns: table => new
                {
                    ProcurementCommunicationResponseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementCommunicationId = table.Column<int>(type: "int", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RespondedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementCommunicationResponses", x => x.ProcurementCommunicationResponseId);
                    table.ForeignKey(
                        name: "FK_ProcurementCommunicationResponses_ProcurementCommunications_ProcurementCommunicationId",
                        column: x => x.ProcurementCommunicationId,
                        principalTable: "ProcurementCommunications",
                        principalColumn: "ProcurementCommunicationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCommunications_TenderId",
                table: "ProcurementCommunications",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCommunicationResponses_ProcurementCommunicationId",
                table: "ProcurementCommunicationResponses",
                column: "ProcurementCommunicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcurementCommunications_Tenders_TenderId",
                table: "ProcurementCommunications",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "TenderId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcurementCommunications_Tenders_TenderId",
                table: "ProcurementCommunications");

            migrationBuilder.DropTable(
                name: "ProcurementCommunicationResponses");

            migrationBuilder.DropIndex(
                name: "IX_ProcurementCommunications_TenderId",
                table: "ProcurementCommunications");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ProcurementCommunications");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                table: "ProcurementCommunications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProcurementCommunications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProcurementCommunications");

            migrationBuilder.DropColumn(
                name: "TenderId",
                table: "ProcurementCommunications");
        }
    }
}
