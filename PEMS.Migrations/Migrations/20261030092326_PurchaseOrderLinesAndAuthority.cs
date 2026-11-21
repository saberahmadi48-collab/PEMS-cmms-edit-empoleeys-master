using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderLinesAndAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserName",
                table: "PurchaseOrders",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApproverRole",
                table: "PurchaseOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorityRuleId",
                table: "PurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaterialRequisitionId",
                table: "PurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "PurchaseOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedByUserName",
                table: "PurchaseOrders",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrderAuthorityRules",
                columns: table => new
                {
                    PurchaseOrderAuthorityRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<int>(type: "int", nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ApproverRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderAuthorityRules", x => x.PurchaseOrderAuthorityRuleId);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PoId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    MaterialRequisitionItemId = table.Column<int>(type: "int", nullable: true),
                    MaterialItemId = table.Column<int>(type: "int", nullable: true),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsStockLine = table.Column<bool>(type: "bit", nullable: false),
                    CommodityGroupId = table.Column<int>(type: "int", nullable: true),
                    FreeTextReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    BidId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.PurchaseOrderLineId);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_MaterialRequisitionItems_MaterialRequisitionItemId",
                        column: x => x.MaterialRequisitionItemId,
                        principalTable: "MaterialRequisitionItems",
                        principalColumn: "MRItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PartCatalogueCommodityGroups_CommodityGroupId",
                        column: x => x.CommodityGroupId,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PoId",
                        column: x => x.PoId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "PoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_AuthorityRuleId",
                table: "PurchaseOrders",
                column: "AuthorityRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_MaterialRequisitionId",
                table: "PurchaseOrders",
                column: "MaterialRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderAuthorityRules_Currency_Level",
                table: "PurchaseOrderAuthorityRules",
                columns: new[] { "Currency", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CommodityGroupId",
                table: "PurchaseOrderLines",
                column: "CommodityGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_MaterialItemId",
                table: "PurchaseOrderLines",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_MaterialRequisitionItemId",
                table: "PurchaseOrderLines",
                column: "MaterialRequisitionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PoId_LineNo",
                table: "PurchaseOrderLines",
                columns: new[] { "PoId", "LineNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_MaterialRequisitions_MaterialRequisitionId",
                table: "PurchaseOrders",
                column: "MaterialRequisitionId",
                principalTable: "MaterialRequisitions",
                principalColumn: "MRId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_PurchaseOrderAuthorityRules_AuthorityRuleId",
                table: "PurchaseOrders",
                column: "AuthorityRuleId",
                principalTable: "PurchaseOrderAuthorityRules",
                principalColumn: "PurchaseOrderAuthorityRuleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_MaterialRequisitions_MaterialRequisitionId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_PurchaseOrderAuthorityRules_AuthorityRuleId",
                table: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "PurchaseOrderAuthorityRules");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_AuthorityRuleId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_MaterialRequisitionId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserName",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ApproverRole",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "AuthorityRuleId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MaterialRequisitionId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserName",
                table: "PurchaseOrders");
        }
    }
}
