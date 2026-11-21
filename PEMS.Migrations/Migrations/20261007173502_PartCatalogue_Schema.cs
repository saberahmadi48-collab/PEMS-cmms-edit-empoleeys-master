using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartCatalogue_Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartCatalogueApproverAssignments",
                columns: table => new
                {
                    ApproverAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueApproverAssignments", x => x.ApproverAssignmentId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueApproverAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueCommodityGroups",
                columns: table => new
                {
                    CommodityGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NeedsConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueCommodityGroups", x => x.CommodityGroupId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueCurrencies",
                columns: table => new
                {
                    CurrencyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueCurrencies", x => x.CurrencyId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueManufacturers",
                columns: table => new
                {
                    ManufacturerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsLocal = table.Column<bool>(type: "bit", nullable: false),
                    IsWrong = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueManufacturers", x => x.ManufacturerId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueOrgLinks",
                columns: table => new
                {
                    OrgLinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ManagerEmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueOrgLinks", x => x.OrgLinkId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueOrgLinks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueOrgLinks_Employees_ManagerEmployeeId",
                        column: x => x.ManagerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartCataloguePlanningMethods",
                columns: table => new
                {
                    PlanningMethodId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCataloguePlanningMethods", x => x.PlanningMethodId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueProductFamilies",
                columns: table => new
                {
                    ProductFamilyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueProductFamilies", x => x.ProductFamilyId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueSections",
                columns: table => new
                {
                    SectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RequiresSparePartsReview = table.Column<bool>(type: "bit", nullable: false),
                    RoutingNeedsConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueSections", x => x.SectionId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueStockGrades",
                columns: table => new
                {
                    StockGradeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DescriptionFa = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueStockGrades", x => x.StockGradeId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueSuppliers",
                columns: table => new
                {
                    SupplierId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueSuppliers", x => x.SupplierId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueTechnicalClasses",
                columns: table => new
                {
                    TechnicalClassId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LongDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueTechnicalClasses", x => x.TechnicalClassId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueTypeDesignations",
                columns: table => new
                {
                    TypeDesignationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    IsCapital = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueTypeDesignations", x => x.TypeDesignationId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueWorkflowSteps",
                columns: table => new
                {
                    WorkflowStepId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApproverSource = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ConditionKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NeedsConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueWorkflowSteps", x => x.WorkflowStepId);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueCommodityGroupChildren",
                columns: table => new
                {
                    CommodityGroupChildId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentGroupId = table.Column<int>(type: "int", nullable: false),
                    ChildGroupId = table.Column<int>(type: "int", nullable: false),
                    NeedsConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueCommodityGroupChildren", x => x.CommodityGroupChildId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueCommodityGroupChildren_PartCatalogueCommodityGroups_ChildGroupId",
                        column: x => x.ChildGroupId,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueCommodityGroupChildren_PartCatalogueCommodityGroups_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueSectionCommodityGroups",
                columns: table => new
                {
                    SectionCommodityGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SectionId = table.Column<int>(type: "int", nullable: false),
                    CommodityGroupId = table.Column<int>(type: "int", nullable: false),
                    NeedsConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueSectionCommodityGroups", x => x.SectionCommodityGroupId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueSectionCommodityGroups_PartCatalogueCommodityGroups_CommodityGroupId",
                        column: x => x.CommodityGroupId,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueSectionCommodityGroups_PartCatalogueSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "PartCatalogueSections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequests",
                columns: table => new
                {
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RequestType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentStepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Urgent = table.Column<bool>(type: "bit", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: true),
                    RequesterEmployeeId = table.Column<int>(type: "int", nullable: false),
                    IsNewEquipment = table.Column<bool>(type: "bit", nullable: false),
                    TechnicalClassId = table.Column<int>(type: "int", nullable: true),
                    AssetType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CommodityGroup1Id = table.Column<int>(type: "int", nullable: true),
                    CommodityGroup2Id = table.Column<int>(type: "int", nullable: true),
                    ProductFamilyId = table.Column<int>(type: "int", nullable: true),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: true),
                    ManufacturerId = table.Column<int>(type: "int", nullable: true),
                    IsNewManufacturer = table.Column<bool>(type: "bit", nullable: false),
                    NewManufacturerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ManufacturerPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PartPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CurrencyId = table.Column<int>(type: "int", nullable: true),
                    TypeDesignationId = table.Column<int>(type: "int", nullable: true),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    IsCapital = table.Column<bool>(type: "bit", nullable: false),
                    SparePartSerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EquipmentSerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufacturerDrawingNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MinimumStock = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    MaxConsumptionRate = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    FirstPurchaseQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PlanningMethodId = table.Column<int>(type: "int", nullable: true),
                    PrimaryIssuedRequired = table.Column<bool>(type: "bit", nullable: false),
                    IssuedQty = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    StockGradeId = table.Column<int>(type: "int", nullable: true),
                    WantedReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StorageVolume = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsRd = table.Column<bool>(type: "bit", nullable: false),
                    ExistingPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChangesMatchInventory = table.Column<bool>(type: "bit", nullable: true),
                    OldManufacturerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OldManufacturerPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AssignedPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequests", x => x.PartCatalogueRequestId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_Employees_RequesterEmployeeId",
                        column: x => x.RequesterEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_MaterialUOMs_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "MaterialUOMs",
                        principalColumn: "MaterialUOMId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueCommodityGroups_CommodityGroup1Id",
                        column: x => x.CommodityGroup1Id,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueCommodityGroups_CommodityGroup2Id",
                        column: x => x.CommodityGroup2Id,
                        principalTable: "PartCatalogueCommodityGroups",
                        principalColumn: "CommodityGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueCurrencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "PartCatalogueCurrencies",
                        principalColumn: "CurrencyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueManufacturers_ManufacturerId",
                        column: x => x.ManufacturerId,
                        principalTable: "PartCatalogueManufacturers",
                        principalColumn: "ManufacturerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCataloguePlanningMethods_PlanningMethodId",
                        column: x => x.PlanningMethodId,
                        principalTable: "PartCataloguePlanningMethods",
                        principalColumn: "PlanningMethodId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueProductFamilies_ProductFamilyId",
                        column: x => x.ProductFamilyId,
                        principalTable: "PartCatalogueProductFamilies",
                        principalColumn: "ProductFamilyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "PartCatalogueSections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueStockGrades_StockGradeId",
                        column: x => x.StockGradeId,
                        principalTable: "PartCatalogueStockGrades",
                        principalColumn: "StockGradeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueSuppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "PartCatalogueSuppliers",
                        principalColumn: "SupplierId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueTechnicalClasses_TechnicalClassId",
                        column: x => x.TechnicalClassId,
                        principalTable: "PartCatalogueTechnicalClasses",
                        principalColumn: "TechnicalClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequests_PartCatalogueTypeDesignations_TypeDesignationId",
                        column: x => x.TypeDesignationId,
                        principalTable: "PartCatalogueTypeDesignations",
                        principalColumn: "TypeDesignationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestApprovals",
                columns: table => new
                {
                    RequestApprovalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    StepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ActorEmployeeId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ActionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestApprovals", x => x.RequestApprovalId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestApprovals_Employees_ActorEmployeeId",
                        column: x => x.ActorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestApprovals_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestAttachments",
                columns: table => new
                {
                    RequestAttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UploadedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    UploadedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestAttachments", x => x.RequestAttachmentId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestAttachments_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestAttributes",
                columns: table => new
                {
                    RequestAttributeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestAttributes", x => x.RequestAttributeId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestAttributes_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestEquipment",
                columns: table => new
                {
                    RequestEquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    EquipmentText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InstalledQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestEquipment", x => x.RequestEquipmentId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestEquipment_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestEquipment_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestNotes",
                columns: table => new
                {
                    RequestNoteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorEmployeeId = table.Column<int>(type: "int", nullable: false),
                    StepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestNotes", x => x.RequestNoteId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestNotes_Employees_AuthorEmployeeId",
                        column: x => x.AuthorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestNotes_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCatalogueRequestSteps",
                columns: table => new
                {
                    RequestStepId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartCatalogueRequestId = table.Column<int>(type: "int", nullable: false),
                    StepKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SkipReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ActivatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogueRequestSteps", x => x.RequestStepId);
                    table.ForeignKey(
                        name: "FK_PartCatalogueRequestSteps_PartCatalogueRequests_PartCatalogueRequestId",
                        column: x => x.PartCatalogueRequestId,
                        principalTable: "PartCatalogueRequests",
                        principalColumn: "PartCatalogueRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueApproverAssignments_EmployeeId",
                table: "PartCatalogueApproverAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueApproverAssignments_StepKey_EmployeeId",
                table: "PartCatalogueApproverAssignments",
                columns: new[] { "StepKey", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueCommodityGroupChildren_ChildGroupId",
                table: "PartCatalogueCommodityGroupChildren",
                column: "ChildGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueCommodityGroupChildren_ParentGroupId_ChildGroupId",
                table: "PartCatalogueCommodityGroupChildren",
                columns: new[] { "ParentGroupId", "ChildGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueCommodityGroups_Code",
                table: "PartCatalogueCommodityGroups",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueCurrencies_Code",
                table: "PartCatalogueCurrencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueManufacturers_Name",
                table: "PartCatalogueManufacturers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueManufacturers_NormalizedName",
                table: "PartCatalogueManufacturers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueOrgLinks_EmployeeId",
                table: "PartCatalogueOrgLinks",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueOrgLinks_ManagerEmployeeId",
                table: "PartCatalogueOrgLinks",
                column: "ManagerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCataloguePlanningMethods_Code",
                table: "PartCataloguePlanningMethods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueProductFamilies_Code",
                table: "PartCatalogueProductFamilies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestApprovals_ActorEmployeeId",
                table: "PartCatalogueRequestApprovals",
                column: "ActorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestApprovals_PartCatalogueRequestId",
                table: "PartCatalogueRequestApprovals",
                column: "PartCatalogueRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestAttachments_PartCatalogueRequestId",
                table: "PartCatalogueRequestAttachments",
                column: "PartCatalogueRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestAttributes_PartCatalogueRequestId_Name",
                table: "PartCatalogueRequestAttributes",
                columns: new[] { "PartCatalogueRequestId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestEquipment_EquipmentId",
                table: "PartCatalogueRequestEquipment",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestEquipment_PartCatalogueRequestId",
                table: "PartCatalogueRequestEquipment",
                column: "PartCatalogueRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestNotes_AuthorEmployeeId",
                table: "PartCatalogueRequestNotes",
                column: "AuthorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestNotes_PartCatalogueRequestId",
                table: "PartCatalogueRequestNotes",
                column: "PartCatalogueRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_CommodityGroup1Id",
                table: "PartCatalogueRequests",
                column: "CommodityGroup1Id");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_CommodityGroup2Id",
                table: "PartCatalogueRequests",
                column: "CommodityGroup2Id");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_CurrencyId",
                table: "PartCatalogueRequests",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_CurrentStepKey",
                table: "PartCatalogueRequests",
                column: "CurrentStepKey");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_ExistingPartNumber",
                table: "PartCatalogueRequests",
                column: "ExistingPartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_ManufacturerId",
                table: "PartCatalogueRequests",
                column: "ManufacturerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_PlanningMethodId",
                table: "PartCatalogueRequests",
                column: "PlanningMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_ProductFamilyId",
                table: "PartCatalogueRequests",
                column: "ProductFamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_RequesterEmployeeId",
                table: "PartCatalogueRequests",
                column: "RequesterEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_RequestNumber",
                table: "PartCatalogueRequests",
                column: "RequestNumber",
                unique: true,
                filter: "[RequestNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_SectionId",
                table: "PartCatalogueRequests",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_Status_Urgent_CreatedDate",
                table: "PartCatalogueRequests",
                columns: new[] { "Status", "Urgent", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_StockGradeId",
                table: "PartCatalogueRequests",
                column: "StockGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_SupplierId",
                table: "PartCatalogueRequests",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_TechnicalClassId",
                table: "PartCatalogueRequests",
                column: "TechnicalClassId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_TypeDesignationId",
                table: "PartCatalogueRequests",
                column: "TypeDesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequests_UnitOfMeasureId",
                table: "PartCatalogueRequests",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestSteps_PartCatalogueRequestId_StepOrder",
                table: "PartCatalogueRequestSteps",
                columns: new[] { "PartCatalogueRequestId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueRequestSteps_Status_StepKey_AssignedEmployeeId",
                table: "PartCatalogueRequestSteps",
                columns: new[] { "Status", "StepKey", "AssignedEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueSectionCommodityGroups_CommodityGroupId",
                table: "PartCatalogueSectionCommodityGroups",
                column: "CommodityGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueSectionCommodityGroups_SectionId_CommodityGroupId",
                table: "PartCatalogueSectionCommodityGroups",
                columns: new[] { "SectionId", "CommodityGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueSections_Code",
                table: "PartCatalogueSections",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueStockGrades_Grade",
                table: "PartCatalogueStockGrades",
                column: "Grade",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueSuppliers_Name",
                table: "PartCatalogueSuppliers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueSuppliers_NormalizedName",
                table: "PartCatalogueSuppliers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueTechnicalClasses_Code",
                table: "PartCatalogueTechnicalClasses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueTechnicalClasses_Description",
                table: "PartCatalogueTechnicalClasses",
                column: "Description");

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueTypeDesignations_Code",
                table: "PartCatalogueTypeDesignations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueWorkflowSteps_StepKey",
                table: "PartCatalogueWorkflowSteps",
                column: "StepKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogueWorkflowSteps_StepOrder",
                table: "PartCatalogueWorkflowSteps",
                column: "StepOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartCatalogueApproverAssignments");

            migrationBuilder.DropTable(
                name: "PartCatalogueCommodityGroupChildren");

            migrationBuilder.DropTable(
                name: "PartCatalogueOrgLinks");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestApprovals");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestAttachments");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestAttributes");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestEquipment");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestNotes");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequestSteps");

            migrationBuilder.DropTable(
                name: "PartCatalogueSectionCommodityGroups");

            migrationBuilder.DropTable(
                name: "PartCatalogueWorkflowSteps");

            migrationBuilder.DropTable(
                name: "PartCatalogueRequests");

            migrationBuilder.DropTable(
                name: "PartCatalogueCommodityGroups");

            migrationBuilder.DropTable(
                name: "PartCatalogueCurrencies");

            migrationBuilder.DropTable(
                name: "PartCatalogueManufacturers");

            migrationBuilder.DropTable(
                name: "PartCataloguePlanningMethods");

            migrationBuilder.DropTable(
                name: "PartCatalogueProductFamilies");

            migrationBuilder.DropTable(
                name: "PartCatalogueSections");

            migrationBuilder.DropTable(
                name: "PartCatalogueStockGrades");

            migrationBuilder.DropTable(
                name: "PartCatalogueSuppliers");

            migrationBuilder.DropTable(
                name: "PartCatalogueTechnicalClasses");

            migrationBuilder.DropTable(
                name: "PartCatalogueTypeDesignations");
        }
    }
}
