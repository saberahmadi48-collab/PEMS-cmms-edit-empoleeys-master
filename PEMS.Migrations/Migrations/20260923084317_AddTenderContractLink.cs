using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenderContractLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "Tenders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_ContractId",
                table: "Tenders",
                column: "ContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_Contracts_ContractId",
                table: "Tenders",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "ContractId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Contracts_ContractId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_ContractId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "Tenders");
        }
    }
}
