using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationRequisitionAndMrsImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MrsImportBatches",
                columns: table => new
                {
                    MrsImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SourceSheet = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MrsImportBatches", x => x.MrsImportBatchId);
                    table.ForeignKey(
                        name: "FK_MrsImportBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreRequisitions",
                columns: table => new
                {
                    StoreRequisitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequisitionNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRequisitions", x => x.StoreRequisitionId);
                    table.ForeignKey(
                        name: "FK_StoreRequisitions_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitions_Employees_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitions_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderMaterials",
                columns: table => new
                {
                    WorkOrderMaterialId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderMaterials", x => x.WorkOrderMaterialId);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterials_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterials_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterials_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MrsImportRows",
                columns: table => new
                {
                    MrsImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MrsImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RawMrsNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawItemCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawPackingNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawPoNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawSupplier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawUom = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawQtyReceived = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    RawQtyIssued = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    RawInventory = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    RawDateReceived = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawDateIssued = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawMivNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawMrvNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawDiscipline = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawSize = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawEquipmentTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawExtraJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedWarehouseCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedWarehouseId = table.Column<int>(type: "int", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MatchedMaterialItemId = table.Column<int>(type: "int", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultMaterialItemId = table.Column<int>(type: "int", nullable: true),
                    ResultInventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MrsImportRows", x => x.MrsImportRowId);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_InventoryTransactions_ResultInventoryTransactionId",
                        column: x => x.ResultInventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "InventoryTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_MaterialItems_MatchedMaterialItemId",
                        column: x => x.MatchedMaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_MaterialItems_ResultMaterialItemId",
                        column: x => x.ResultMaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_MrsImportBatches_MrsImportBatchId",
                        column: x => x.MrsImportBatchId,
                        principalTable: "MrsImportBatches",
                        principalColumn: "MrsImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MrsImportRows_Warehouses_ResolvedWarehouseId",
                        column: x => x.ResolvedWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialReservations",
                columns: table => new
                {
                    MaterialReservationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    MaintenancePlanId = table.Column<int>(type: "int", nullable: true),
                    StoreRequisitionId = table.Column<int>(type: "int", nullable: true),
                    ReservedById = table.Column<int>(type: "int", nullable: false),
                    ReservedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialReservations", x => x.MaterialReservationId);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_Employees_ReservedById",
                        column: x => x.ReservedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_MaintenancePlans_MaintenancePlanId",
                        column: x => x.MaintenancePlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "MaintenancePlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_StoreRequisitions_StoreRequisitionId",
                        column: x => x.StoreRequisitionId,
                        principalTable: "StoreRequisitions",
                        principalColumn: "StoreRequisitionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialReservations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreRequisitionLines",
                columns: table => new
                {
                    StoreRequisitionLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreRequisitionId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    IssuedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRequisitionLines", x => x.StoreRequisitionLineId);
                    table.ForeignKey(
                        name: "FK_StoreRequisitionLines_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitionLines_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitionLines_StoreRequisitions_StoreRequisitionId",
                        column: x => x.StoreRequisitionId,
                        principalTable: "StoreRequisitions",
                        principalColumn: "StoreRequisitionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreRequisitionStatusHistories",
                columns: table => new
                {
                    StoreRequisitionStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreRequisitionId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRequisitionStatusHistories", x => x.StoreRequisitionStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_StoreRequisitionStatusHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreRequisitionStatusHistories_StoreRequisitions_StoreRequisitionId",
                        column: x => x.StoreRequisitionId,
                        principalTable: "StoreRequisitions",
                        principalColumn: "StoreRequisitionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_MaintenancePlanId",
                table: "MaterialReservations",
                column: "MaintenancePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_MaterialItemId_Status",
                table: "MaterialReservations",
                columns: new[] { "MaterialItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_ReservedById",
                table: "MaterialReservations",
                column: "ReservedById");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_StoreRequisitionId",
                table: "MaterialReservations",
                column: "StoreRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_WarehouseId",
                table: "MaterialReservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_WorkOrderId",
                table: "MaterialReservations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportBatches_ImportedById",
                table: "MrsImportBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_MatchedMaterialItemId",
                table: "MrsImportRows",
                column: "MatchedMaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_MrsImportBatchId",
                table: "MrsImportRows",
                column: "MrsImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_ResolvedWarehouseId",
                table: "MrsImportRows",
                column: "ResolvedWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_ResultInventoryTransactionId",
                table: "MrsImportRows",
                column: "ResultInventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_ResultMaterialItemId",
                table: "MrsImportRows",
                column: "ResultMaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MrsImportRows_ReviewedById",
                table: "MrsImportRows",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionLines_MaterialItemId",
                table: "StoreRequisitionLines",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionLines_StoreRequisitionId",
                table: "StoreRequisitionLines",
                column: "StoreRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionLines_UomId",
                table: "StoreRequisitionLines",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitions_ApprovedById",
                table: "StoreRequisitions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitions_RequestedById",
                table: "StoreRequisitions",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitions_RequisitionNumber",
                table: "StoreRequisitions",
                column: "RequisitionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitions_WarehouseId",
                table: "StoreRequisitions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitions_WorkOrderId",
                table: "StoreRequisitions",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionStatusHistories_ChangedById",
                table: "StoreRequisitionStatusHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRequisitionStatusHistories_StoreRequisitionId",
                table: "StoreRequisitionStatusHistories",
                column: "StoreRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterials_MaterialItemId",
                table: "WorkOrderMaterials",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterials_UomId",
                table: "WorkOrderMaterials",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterials_WorkOrderId_MaterialItemId",
                table: "WorkOrderMaterials",
                columns: new[] { "WorkOrderId", "MaterialItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialReservations");

            migrationBuilder.DropTable(
                name: "MrsImportRows");

            migrationBuilder.DropTable(
                name: "StoreRequisitionLines");

            migrationBuilder.DropTable(
                name: "StoreRequisitionStatusHistories");

            migrationBuilder.DropTable(
                name: "WorkOrderMaterials");

            migrationBuilder.DropTable(
                name: "MrsImportBatches");

            migrationBuilder.DropTable(
                name: "StoreRequisitions");
        }
    }
}
