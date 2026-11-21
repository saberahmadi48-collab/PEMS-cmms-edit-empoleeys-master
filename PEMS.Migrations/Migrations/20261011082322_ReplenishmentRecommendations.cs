using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplenishmentRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetDemandRuns",
                columns: table => new
                {
                    NetDemandRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequestedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CalculatorVersion = table.Column<int>(type: "int", nullable: false),
                    ScopeMaterialItemIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ScopeWarehouseIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResultsEvaluated = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Unchanged = table.Column<int>(type: "int", nullable: false),
                    Closed = table.Column<int>(type: "int", nullable: false),
                    AutoReopened = table.Column<int>(type: "int", nullable: false),
                    NotDefinitive = table.Column<int>(type: "int", nullable: false),
                    NotComparableNoWarehouse = table.Column<int>(type: "int", nullable: false),
                    NotDefinitiveSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetDemandRuns", x => x.NetDemandRunId);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentRecommendations",
                columns: table => new
                {
                    ReplenishmentRecommendationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    UomId = table.Column<int>(type: "int", nullable: true),
                    OnHandQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UsableQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    GrossEligibleDemand = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CoveredByReservations = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnreservedDemand = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ShortageQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    RecommendedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    NoticeCodes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsCurrentlyDefinitive = table.Column<bool>(type: "bit", nullable: false),
                    BlockingIssueCodes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StockLastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DemandLastChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstRunId = table.Column<int>(type: "int", nullable: false),
                    LastRunId = table.Column<int>(type: "int", nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEvaluatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DismissedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DismissedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DismissedShortageQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    DismissedSourceKeys = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentRecommendations", x => x.ReplenishmentRecommendationId);
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendations_MaterialItems_MaterialItemId",
                        column: x => x.MaterialItemId,
                        principalTable: "MaterialItems",
                        principalColumn: "MaterialItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendations_NetDemandRuns_FirstRunId",
                        column: x => x.FirstRunId,
                        principalTable: "NetDemandRuns",
                        principalColumn: "NetDemandRunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendations_NetDemandRuns_LastRunId",
                        column: x => x.LastRunId,
                        principalTable: "NetDemandRuns",
                        principalColumn: "NetDemandRunId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentRecommendationHistories",
                columns: table => new
                {
                    ReplenishmentRecommendationHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReplenishmentRecommendationId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ShortageQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UsableQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnreservedDemand = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RunId = table.Column<int>(type: "int", nullable: true),
                    ActorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentRecommendationHistories", x => x.ReplenishmentRecommendationHistoryId);
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendationHistories_ReplenishmentRecommendations_ReplenishmentRecommendationId",
                        column: x => x.ReplenishmentRecommendationId,
                        principalTable: "ReplenishmentRecommendations",
                        principalColumn: "ReplenishmentRecommendationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplenishmentRecommendationSources",
                columns: table => new
                {
                    ReplenishmentRecommendationSourceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReplenishmentRecommendationId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    StoreRequisitionLineId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderMaterialId = table.Column<int>(type: "int", nullable: true),
                    GrossRemainingQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CoveredByReservation = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnreservedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplenishmentRecommendationSources", x => x.ReplenishmentRecommendationSourceId);
                    table.CheckConstraint("CK_ReplenishmentRecommendationSources_OneSource", "([SourceType] = 1 AND [StoreRequisitionLineId] IS NOT NULL AND [WorkOrderMaterialId] IS NULL) OR ([SourceType] = 2 AND [WorkOrderMaterialId] IS NOT NULL AND [StoreRequisitionLineId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_ReplenishmentRecommendationSources_ReplenishmentRecommendations_ReplenishmentRecommendationId",
                        column: x => x.ReplenishmentRecommendationId,
                        principalTable: "ReplenishmentRecommendations",
                        principalColumn: "ReplenishmentRecommendationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_NetDemandRuns_RequestKey",
                table: "NetDemandRuns",
                column: "RequestKey",
                unique: true,
                filter: "[RequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendationHistories_ReplenishmentRecommendationId_Revision_ReplenishmentRecommendationHistoryId",
                table: "ReplenishmentRecommendationHistories",
                columns: new[] { "ReplenishmentRecommendationId", "Revision", "ReplenishmentRecommendationHistoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendations_FirstRunId",
                table: "ReplenishmentRecommendations",
                column: "FirstRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendations_LastRunId",
                table: "ReplenishmentRecommendations",
                column: "LastRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendations_Status",
                table: "ReplenishmentRecommendations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendations_WarehouseId",
                table: "ReplenishmentRecommendations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_ReplenishmentRecommendations_Live",
                table: "ReplenishmentRecommendations",
                columns: new[] { "MaterialItemId", "WarehouseId" },
                unique: true,
                filter: "[Status] IN (N'Open', N'Dismissed')");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendationSources_ReplenishmentRecommendationId_Revision",
                table: "ReplenishmentRecommendationSources",
                columns: new[] { "ReplenishmentRecommendationId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendationSources_StoreRequisitionLineId",
                table: "ReplenishmentRecommendationSources",
                column: "StoreRequisitionLineId",
                filter: "[StoreRequisitionLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReplenishmentRecommendationSources_WorkOrderMaterialId",
                table: "ReplenishmentRecommendationSources",
                column: "WorkOrderMaterialId",
                filter: "[WorkOrderMaterialId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplenishmentRecommendationHistories");

            migrationBuilder.DropTable(
                name: "ReplenishmentRecommendationSources");

            migrationBuilder.DropTable(
                name: "ReplenishmentRecommendations");

            migrationBuilder.DropTable(
                name: "NetDemandRuns");
        }
    }
}
