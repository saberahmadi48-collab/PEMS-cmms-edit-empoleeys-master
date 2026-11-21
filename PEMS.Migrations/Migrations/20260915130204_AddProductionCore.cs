using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionWorkCenters",
                columns: table => new
                {
                    ProductionWorkCenterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionWorkCenters", x => x.ProductionWorkCenterId);
                    table.ForeignKey(
                        name: "FK_ProductionWorkCenters_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionWorkCenters_FunctionalLocations_FunctionalLocationId",
                        column: x => x.FunctionalLocationId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftDefinitions",
                columns: table => new
                {
                    ShiftDefinitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftDefinitions", x => x.ShiftDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    PlannedStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedFinishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualFinishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreHoldStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductionWorkCenterId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    ResponsibleEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HoldReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.ProductionOrderId);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Employees_ResponsibleEmployeeId",
                        column: x => x.ResponsibleEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_ProductionWorkCenters_ProductionWorkCenterId",
                        column: x => x.ProductionWorkCenterId,
                        principalTable: "ProductionWorkCenters",
                        principalColumn: "ProductionWorkCenterId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRuns",
                columns: table => new
                {
                    ProductionRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreHoldStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductionWorkCenterId = table.Column<int>(type: "int", nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    OperatorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ShiftDefinitionId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HoldReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRuns", x => x.ProductionRunId);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Employees_OperatorEmployeeId",
                        column: x => x.OperatorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "ProductionOrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_ProductionWorkCenters_ProductionWorkCenterId",
                        column: x => x.ProductionWorkCenterId,
                        principalTable: "ProductionWorkCenters",
                        principalColumn: "ProductionWorkCenterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_ShiftDefinitions_ShiftDefinitionId",
                        column: x => x.ShiftDefinitionId,
                        principalTable: "ShiftDefinitions",
                        principalColumn: "ShiftDefinitionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionBatches",
                columns: table => new
                {
                    ProductionBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductionRunId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    ProducedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QualityStatusReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QualityStatusChangedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QualityStatusChangedById = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionBatches", x => x.ProductionBatchId);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_Employees_QualityStatusChangedById",
                        column: x => x.QualityStatusChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "ProductionOrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionBatches_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionEvents",
                columns: table => new
                {
                    ProductionEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: true),
                    ProductionRunId = table.Column<int>(type: "int", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionEvents", x => x.ProductionEventId);
                    table.ForeignKey(
                        name: "FK_ProductionEvents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionEvents_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "ProductionOrderId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionEvents_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLosses",
                columns: table => new
                {
                    ProductionLossId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionRunId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    LossCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLosses", x => x.ProductionLossId);
                    table.ForeignKey(
                        name: "FK_ProductionLosses_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionLosses_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionLosses_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionInputs",
                columns: table => new
                {
                    ProductionInputId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionRunId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    SourceProductionBatchId = table.Column<int>(type: "int", nullable: true),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionInputs", x => x.ProductionInputId);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "InventoryTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_ProductionBatches_SourceProductionBatchId",
                        column: x => x.SourceProductionBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "ProductionBatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionInputs_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOutputs",
                columns: table => new
                {
                    ProductionOutputId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionRunId = table.Column<int>(type: "int", nullable: false),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    ProductionBatchId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    OutputClassification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOutputs", x => x.ProductionOutputId);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "InventoryTransactionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_MaterialUOMs_UomId",
                        column: x => x.UomId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_ProductionBatches_ProductionBatchId",
                        column: x => x.ProductionBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "ProductionBatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOutputs_ProductionRuns_ProductionRunId",
                        column: x => x.ProductionRunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "ProductionRunId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_BatchNumber",
                table: "ProductionBatches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_MaterialItemId",
                table: "ProductionBatches",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductionOrderId",
                table: "ProductionBatches",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_ProductionRunId",
                table: "ProductionBatches",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_QualityStatus",
                table: "ProductionBatches",
                column: "QualityStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_QualityStatusChangedById",
                table: "ProductionBatches",
                column: "QualityStatusChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionBatches_UomId",
                table: "ProductionBatches",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEvents_EmployeeId",
                table: "ProductionEvents",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEvents_ProductionOrderId",
                table: "ProductionEvents",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEvents_ProductionRunId",
                table: "ProductionEvents",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_EmployeeId",
                table: "ProductionInputs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_InventoryTransactionId",
                table: "ProductionInputs",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_MaterialItemId",
                table: "ProductionInputs",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_ProductionRunId",
                table: "ProductionInputs",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_SourceProductionBatchId",
                table: "ProductionInputs",
                column: "SourceProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionInputs_UomId",
                table: "ProductionInputs",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLosses_EmployeeId",
                table: "ProductionLosses",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLosses_ProductionRunId",
                table: "ProductionLosses",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLosses_UomId",
                table: "ProductionLosses",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_EquipmentId",
                table: "ProductionOrders",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_MaterialItemId",
                table: "ProductionOrders",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrderNumber",
                table: "ProductionOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductionWorkCenterId",
                table: "ProductionOrders",
                column: "ProductionWorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ResponsibleEmployeeId",
                table: "ProductionOrders",
                column: "ResponsibleEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_Status",
                table: "ProductionOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_UomId",
                table: "ProductionOrders",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_EmployeeId",
                table: "ProductionOutputs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_InventoryTransactionId",
                table: "ProductionOutputs",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_MaterialItemId",
                table: "ProductionOutputs",
                column: "MaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_ProductionBatchId",
                table: "ProductionOutputs",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_ProductionRunId",
                table: "ProductionOutputs",
                column: "ProductionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOutputs_UomId",
                table: "ProductionOutputs",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_EquipmentId",
                table: "ProductionRuns",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_OperatorEmployeeId",
                table: "ProductionRuns",
                column: "OperatorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProductionOrderId",
                table: "ProductionRuns",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProductionWorkCenterId",
                table: "ProductionRuns",
                column: "ProductionWorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_RunNumber",
                table: "ProductionRuns",
                column: "RunNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ShiftDefinitionId",
                table: "ProductionRuns",
                column: "ShiftDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_Status",
                table: "ProductionRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionWorkCenters_Code",
                table: "ProductionWorkCenters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionWorkCenters_EquipmentId",
                table: "ProductionWorkCenters",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionWorkCenters_FunctionalLocationId",
                table: "ProductionWorkCenters",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftDefinitions_Code",
                table: "ShiftDefinitions",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionEvents");

            migrationBuilder.DropTable(
                name: "ProductionInputs");

            migrationBuilder.DropTable(
                name: "ProductionLosses");

            migrationBuilder.DropTable(
                name: "ProductionOutputs");

            migrationBuilder.DropTable(
                name: "ProductionBatches");

            migrationBuilder.DropTable(
                name: "ProductionRuns");

            migrationBuilder.DropTable(
                name: "ProductionOrders");

            migrationBuilder.DropTable(
                name: "ShiftDefinitions");

            migrationBuilder.DropTable(
                name: "ProductionWorkCenters");
        }
    }
}
