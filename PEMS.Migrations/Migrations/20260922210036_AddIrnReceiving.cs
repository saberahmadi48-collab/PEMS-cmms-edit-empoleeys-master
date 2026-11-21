using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIrnReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IrnReceipts",
                columns: table => new
                {
                    IrnReceiptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IrnNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    ReceivingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    MrsImportBatchId = table.Column<int>(type: "int", nullable: true),
                    ReceivedById = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrnReceipts", x => x.IrnReceiptId);
                    table.ForeignKey(
                        name: "FK_IrnReceipts_Employees_ReceivedById",
                        column: x => x.ReceivedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceipts_MrsImportBatches_MrsImportBatchId",
                        column: x => x.MrsImportBatchId,
                        principalTable: "MrsImportBatches",
                        principalColumn: "MrsImportBatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "PoId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceipts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceipts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IrnReceiptLines",
                columns: table => new
                {
                    IrnReceiptLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IrnReceiptId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    StorageLocationId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrnReceiptLines", x => x.IrnReceiptLineId);
                    table.ForeignKey(
                        name: "FK_IrnReceiptLines_IrnReceipts_IrnReceiptId",
                        column: x => x.IrnReceiptId,
                        principalTable: "IrnReceipts",
                        principalColumn: "IrnReceiptId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IrnReceiptLines_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceiptLines_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceiptLines_StorageLocations_StorageLocationId",
                        column: x => x.StorageLocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "StorageLocationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IrnReceiptStatusHistories",
                columns: table => new
                {
                    IrnReceiptStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IrnReceiptId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrnReceiptStatusHistories", x => x.IrnReceiptStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_IrnReceiptStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IrnReceiptStatusHistories_IrnReceipts_IrnReceiptId",
                        column: x => x.IrnReceiptId,
                        principalTable: "IrnReceipts",
                        principalColumn: "IrnReceiptId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptLines_IrnReceiptId",
                table: "IrnReceiptLines",
                column: "IrnReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptLines_MaterialItemId",
                table: "IrnReceiptLines",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptLines_StorageLocationId",
                table: "IrnReceiptLines",
                column: "StorageLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptLines_UomId",
                table: "IrnReceiptLines",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_IrnNumber",
                table: "IrnReceipts",
                column: "IrnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_MrsImportBatchId",
                table: "IrnReceipts",
                column: "MrsImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_PurchaseOrderId",
                table: "IrnReceipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_ReceivedById",
                table: "IrnReceipts",
                column: "ReceivedById");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_VendorId",
                table: "IrnReceipts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceipts_WarehouseId",
                table: "IrnReceipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptStatusHistories_ChangedById",
                table: "IrnReceiptStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_IrnReceiptStatusHistories_IrnReceiptId",
                table: "IrnReceiptStatusHistories",
                column: "IrnReceiptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IrnReceiptLines");

            migrationBuilder.DropTable(
                name: "IrnReceiptStatusHistories");

            migrationBuilder.DropTable(
                name: "IrnReceipts");
        }
    }
}
