using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaterialRequisitionItemRegisteredPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaterialItemId",
                table: "MaterialRequisitionItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartCatalogueRequestId",
                table: "MaterialRequisitionItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionItems_MaterialItemId",
                table: "MaterialRequisitionItems",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionItems_PartCatalogueRequestId",
                table: "MaterialRequisitionItems",
                column: "PartCatalogueRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisitionItems_MaterialItems_MaterialItemId",
                table: "MaterialRequisitionItems",
                column: "MaterialItemId",
                principalTable: "MaterialItems",
                principalColumn: "MaterialItemId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialRequisitionItems_PartCatalogueRequests_PartCatalogueRequestId",
                table: "MaterialRequisitionItems",
                column: "PartCatalogueRequestId",
                principalTable: "PartCatalogueRequests",
                principalColumn: "PartCatalogueRequestId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitionItems_MaterialItems_MaterialItemId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitionItems_PartCatalogueRequests_PartCatalogueRequestId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisitionItems_MaterialItemId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropIndex(
                name: "IX_MaterialRequisitionItems_PartCatalogueRequestId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropColumn(
                name: "MaterialItemId",
                table: "MaterialRequisitionItems");

            migrationBuilder.DropColumn(
                name: "PartCatalogueRequestId",
                table: "MaterialRequisitionItems");
        }
    }
}
