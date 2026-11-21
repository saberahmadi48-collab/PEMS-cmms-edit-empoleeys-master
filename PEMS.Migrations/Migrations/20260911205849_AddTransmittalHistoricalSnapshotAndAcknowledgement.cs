using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransmittalHistoricalSnapshotAndAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "TransmittalRecipients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgedById",
                table: "TransmittalRecipients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgementNote",
                table: "TransmittalRecipients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentAttachmentId",
                table: "TransmittalItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentRevisionId",
                table: "TransmittalItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotFileHash",
                table: "TransmittalItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotFileName",
                table: "TransmittalItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SnapshotFileSize",
                table: "TransmittalItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalRecipients_AcknowledgedById",
                table: "TransmittalRecipients",
                column: "AcknowledgedById");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalItems_DocumentAttachmentId",
                table: "TransmittalItems",
                column: "DocumentAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalItems_DocumentRevisionId",
                table: "TransmittalItems",
                column: "DocumentRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransmittalItems_DocumentAttachments_DocumentAttachmentId",
                table: "TransmittalItems",
                column: "DocumentAttachmentId",
                principalTable: "DocumentAttachments",
                principalColumn: "AttachmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransmittalItems_DocumentRevisions_DocumentRevisionId",
                table: "TransmittalItems",
                column: "DocumentRevisionId",
                principalTable: "DocumentRevisions",
                principalColumn: "RevisionId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransmittalRecipients_Employees_AcknowledgedById",
                table: "TransmittalRecipients",
                column: "AcknowledgedById",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransmittalItems_DocumentAttachments_DocumentAttachmentId",
                table: "TransmittalItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TransmittalItems_DocumentRevisions_DocumentRevisionId",
                table: "TransmittalItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TransmittalRecipients_Employees_AcknowledgedById",
                table: "TransmittalRecipients");

            migrationBuilder.DropIndex(
                name: "IX_TransmittalRecipients_AcknowledgedById",
                table: "TransmittalRecipients");

            migrationBuilder.DropIndex(
                name: "IX_TransmittalItems_DocumentAttachmentId",
                table: "TransmittalItems");

            migrationBuilder.DropIndex(
                name: "IX_TransmittalItems_DocumentRevisionId",
                table: "TransmittalItems");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "TransmittalRecipients");

            migrationBuilder.DropColumn(
                name: "AcknowledgedById",
                table: "TransmittalRecipients");

            migrationBuilder.DropColumn(
                name: "AcknowledgementNote",
                table: "TransmittalRecipients");

            migrationBuilder.DropColumn(
                name: "DocumentAttachmentId",
                table: "TransmittalItems");

            migrationBuilder.DropColumn(
                name: "DocumentRevisionId",
                table: "TransmittalItems");

            migrationBuilder.DropColumn(
                name: "SnapshotFileHash",
                table: "TransmittalItems");

            migrationBuilder.DropColumn(
                name: "SnapshotFileName",
                table: "TransmittalItems");

            migrationBuilder.DropColumn(
                name: "SnapshotFileSize",
                table: "TransmittalItems");
        }
    }
}
