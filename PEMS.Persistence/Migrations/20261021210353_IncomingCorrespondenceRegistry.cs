using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncomingCorrespondenceRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "Letters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrespondentPartyId",
                table: "Letters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentDate",
                table: "Letters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptChannel",
                table: "Letters",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedDate",
                table: "Letters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderReference",
                table: "Letters",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CorrespondentParties",
                columns: table => new
                {
                    CorrespondentPartyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PartyType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentParties", x => x.CorrespondentPartyId);
                    table.ForeignKey(
                        name: "FK_CorrespondentParties_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LetterLinks",
                columns: table => new
                {
                    LetterLinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    TargetNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterLinks", x => x.LetterLinkId);
                    table.ForeignKey(
                        name: "FK_LetterLinks_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ContractId",
                table: "Letters",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_CorrespondentPartyId_SenderReference",
                table: "Letters",
                columns: new[] { "CorrespondentPartyId", "SenderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentParties_Name",
                table: "CorrespondentParties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentParties_VendorId",
                table: "CorrespondentParties",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterLinks_LetterId_LinkType_TargetId",
                table: "LetterLinks",
                columns: new[] { "LetterId", "LinkType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterLinks_LinkType_TargetId",
                table: "LetterLinks",
                columns: new[] { "LinkType", "TargetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Letters_Contracts_ContractId",
                table: "Letters",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "ContractId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Letters_CorrespondentParties_CorrespondentPartyId",
                table: "Letters",
                column: "CorrespondentPartyId",
                principalTable: "CorrespondentParties",
                principalColumn: "CorrespondentPartyId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Letters_Contracts_ContractId",
                table: "Letters");

            migrationBuilder.DropForeignKey(
                name: "FK_Letters_CorrespondentParties_CorrespondentPartyId",
                table: "Letters");

            migrationBuilder.DropTable(
                name: "CorrespondentParties");

            migrationBuilder.DropTable(
                name: "LetterLinks");

            migrationBuilder.DropIndex(
                name: "IX_Letters_ContractId",
                table: "Letters");

            migrationBuilder.DropIndex(
                name: "IX_Letters_CorrespondentPartyId_SenderReference",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "CorrespondentPartyId",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "DocumentDate",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ReceiptChannel",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "ReceivedDate",
                table: "Letters");

            migrationBuilder.DropColumn(
                name: "SenderReference",
                table: "Letters");
        }
    }
}
