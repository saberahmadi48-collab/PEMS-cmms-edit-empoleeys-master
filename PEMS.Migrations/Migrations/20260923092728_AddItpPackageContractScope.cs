using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItpPackageContractScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "InspectionTestPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcurementPackageId",
                table: "InspectionTestPlans",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTestPlans_ContractId",
                table: "InspectionTestPlans",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTestPlans_ProcurementPackageId",
                table: "InspectionTestPlans",
                column: "ProcurementPackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionTestPlans_Contracts_ContractId",
                table: "InspectionTestPlans",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "ContractId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionTestPlans_ProcurementPackages_ProcurementPackageId",
                table: "InspectionTestPlans",
                column: "ProcurementPackageId",
                principalTable: "ProcurementPackages",
                principalColumn: "ProcurementPackageId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionTestPlans_Contracts_ContractId",
                table: "InspectionTestPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionTestPlans_ProcurementPackages_ProcurementPackageId",
                table: "InspectionTestPlans");

            migrationBuilder.DropIndex(
                name: "IX_InspectionTestPlans_ContractId",
                table: "InspectionTestPlans");

            migrationBuilder.DropIndex(
                name: "IX_InspectionTestPlans_ProcurementPackageId",
                table: "InspectionTestPlans");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "InspectionTestPlans");

            migrationBuilder.DropColumn(
                name: "ProcurementPackageId",
                table: "InspectionTestPlans");
        }
    }
}
