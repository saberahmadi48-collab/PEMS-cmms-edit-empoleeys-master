using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIConfidenceSettings",
                columns: table => new
                {
                    AIConfidenceSettingsId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HighThreshold = table.Column<double>(type: "float", nullable: false),
                    MediumThreshold = table.Column<double>(type: "float", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIConfidenceSettings", x => x.AIConfidenceSettingsId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    DisciplineId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityKeyValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedFieldsSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAIAssisted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                });

            migrationBuilder.CreateTable(
                name: "Cables",
                columns: table => new
                {
                    CableId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CableNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CableType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FromEquipment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToEquipment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VoltageLevel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Length = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CoreNumber = table.Column<int>(type: "int", nullable: true),
                    InstallationMethod = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CableTray = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cables", x => x.CableId);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    ChatMessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.ChatMessageId);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "Disciplines",
                columns: table => new
                {
                    DisciplineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImportAliases = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disciplines", x => x.DisciplineId);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringRelationships",
                columns: table => new
                {
                    EngineeringRelationshipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    RelationshipType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    IsAIProposed = table.Column<bool>(type: "bit", nullable: false),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringRelationships", x => x.EngineeringRelationshipId);
                });

            migrationBuilder.CreateTable(
                name: "FunctionalLocations",
                columns: table => new
                {
                    FunctionalLocationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Plant = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalLocations", x => x.FunctionalLocationId);
                    table.ForeignKey(
                        name: "FK_FunctionalLocations_FunctionalLocations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FunctionalLocations",
                        principalColumn: "FunctionalLocationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseEntries",
                columns: table => new
                {
                    KnowledgeBaseEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseEntries", x => x.KnowledgeBaseEntryId);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedId = table.Column<int>(type: "int", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                });

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    PanelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PanelNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PanelType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Voltage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IPRating = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Panels", x => x.PanelId);
                });

            migrationBuilder.CreateTable(
                name: "PipeSpecifications",
                columns: table => new
                {
                    PipeSpecificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rating = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DesignPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipeSpecifications", x => x.PipeSpecificationId);
                });

            migrationBuilder.CreateTable(
                name: "PipingComponents",
                columns: table => new
                {
                    PipingComponentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rating = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LineNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Specification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipingComponents", x => x.PipingComponentId);
                });

            migrationBuilder.CreateTable(
                name: "PipingLines",
                columns: table => new
                {
                    PipingLineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Fluid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PipeClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DesignPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    FromEquipment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToEquipment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipingLines", x => x.PipingLineId);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "QCDisciplines",
                columns: table => new
                {
                    QCDisciplineId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
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
                    table.PrimaryKey("PK_QCDisciplines", x => x.QCDisciplineId);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RolePermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false),
                    CanEdit = table.Column<bool>(type: "bit", nullable: false),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false),
                    CanApprove = table.Column<bool>(type: "bit", nullable: false),
                    CanManualNumber = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.RolePermissionId);
                });

            migrationBuilder.CreateTable(
                name: "RoutingRules",
                columns: table => new
                {
                    RoutingRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetModule = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowAutoRouting = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRules", x => x.RoutingRuleId);
                });

            migrationBuilder.CreateTable(
                name: "SavedFilters",
                columns: table => new
                {
                    SavedFilterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false),
                    PageKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QueryString = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedFilters", x => x.SavedFilterId);
                });

            migrationBuilder.CreateTable(
                name: "SpareParts",
                columns: table => new
                {
                    SparePartId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinStockLevel = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReorderPoint = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    StorageLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpareParts", x => x.SparePartId);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    TagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                });

            migrationBuilder.CreateTable(
                name: "UserDashboardWidgets",
                columns: table => new
                {
                    UserDashboardWidgetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false),
                    WidgetKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDashboardWidgets", x => x.UserDashboardWidgetId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Mobile = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Valves",
                columns: table => new
                {
                    ValveId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValveType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rating = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActuatorType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailPosition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LineNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Valves", x => x.ValveId);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    VendorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiskLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskScore = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: true),
                    RiskAssessedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.VendorId);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                columns: table => new
                {
                    WorkflowDefinitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.WorkflowDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CableConnections",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CableId = table.Column<int>(type: "int", nullable: false),
                    FromPanel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FromTerminal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToEquipment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToTerminal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConnectionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CableConnections", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_CableConnections_Cables_CableId",
                        column: x => x.CableId,
                        principalTable: "Cables",
                        principalColumn: "CableId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentIdcReferrals",
                columns: table => new
                {
                    DocumentIdcReferralId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIdcReferrals", x => x.DocumentIdcReferralId);
                    table.ForeignKey(
                        name: "FK_DocumentIdcReferrals_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonnelCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignaturePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Employees_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRoles",
                columns: table => new
                {
                    MaintenanceRoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsCrewLeadRole = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRoles", x => x.MaintenanceRoleId);
                    table.ForeignKey(
                        name: "FK_MaintenanceRoles_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentKind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommentStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFrozen = table.Column<bool>(type: "bit", nullable: false),
                    FrozenDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AIWorkflowStage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_Documents_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MDRImportBatches",
                columns: table => new
                {
                    MDRImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowsTotal = table.Column<int>(type: "int", nullable: false),
                    RowsValid = table.Column<int>(type: "int", nullable: false),
                    RowsWarning = table.Column<int>(type: "int", nullable: false),
                    RowsError = table.Column<int>(type: "int", nullable: false),
                    RowsDuplicate = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeaderWarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MDRImportBatches", x => x.MDRImportBatchId);
                    table.ForeignKey(
                        name: "FK_MDRImportBatches_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Meetings",
                columns: table => new
                {
                    MeetingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeetingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizerName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    Agenda = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MinutesNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meetings", x => x.MeetingId);
                    table.ForeignKey(
                        name: "FK_Meetings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementPackages",
                columns: table => new
                {
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementPackages", x => x.ProcurementPackageId);
                    table.ForeignKey(
                        name: "FK_ProcurementPackages_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementPackages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VDRImportBatches",
                columns: table => new
                {
                    VDRImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowsTotal = table.Column<int>(type: "int", nullable: false),
                    RowsValid = table.Column<int>(type: "int", nullable: false),
                    RowsWarning = table.Column<int>(type: "int", nullable: false),
                    RowsError = table.Column<int>(type: "int", nullable: false),
                    RowsDuplicate = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommittedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VDRImportBatches", x => x.VDRImportBatchId);
                    table.ForeignKey(
                        name: "FK_VDRImportBatches_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    ContractId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    ScopeOfWork = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.ContractId);
                    table.ForeignKey(
                        name: "FK_Contracts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contracts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowVersions",
                columns: table => new
                {
                    WorkflowVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowVersions", x => x.WorkflowVersionId);
                    table.ForeignKey(
                        name: "FK_WorkflowVersions_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "WorkflowDefinitionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisciplineTasks",
                columns: table => new
                {
                    DisciplineTaskId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsibleEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineTasks", x => x.DisciplineTaskId);
                    table.ForeignKey(
                        name: "FK_DisciplineTasks_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineTasks_Employees_ResponsibleEmployeeId",
                        column: x => x.ResponsibleEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineTasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLaborRates",
                columns: table => new
                {
                    EmployeeLaborRateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    RateType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLaborRates", x => x.EmployeeLaborRateId);
                    table.ForeignKey(
                        name: "FK_EmployeeLaborRates_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentImportBatches",
                columns: table => new
                {
                    EquipmentImportBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportedById = table.Column<int>(type: "int", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentImportBatches", x => x.EquipmentImportBatchId);
                    table.ForeignKey(
                        name: "FK_EquipmentImportBatches_Employees_ImportedById",
                        column: x => x.ImportedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentImportBatches_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskItems",
                columns: table => new
                {
                    TaskItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedToEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AssignedByApplicationUserId = table.Column<int>(type: "int", nullable: true),
                    AssignedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedType = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RelatedId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskItems", x => x.TaskItemId);
                    table.ForeignKey(
                        name: "FK_TaskItems_Employees_AssignedToEmployeeId",
                        column: x => x.AssignedToEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    SupervisorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Shift = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_Teams_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Teams_Employees_SupervisorEmployeeId",
                        column: x => x.SupervisorEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transmittals",
                columns: table => new
                {
                    TransmittalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransmittalNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    From = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    To = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransmittalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentById = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transmittals", x => x.TransmittalId);
                    table.ForeignKey(
                        name: "FK_Transmittals_Employees_SentById",
                        column: x => x.SentById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transmittals_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MDRImportRows",
                columns: table => new
                {
                    MDRImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MDRImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDocumentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MDRImportRows", x => x.MDRImportRowId);
                    table.ForeignKey(
                        name: "FK_MDRImportRows_MDRImportBatches_MDRImportBatchId",
                        column: x => x.MDRImportBatchId,
                        principalTable: "MDRImportBatches",
                        principalColumn: "MDRImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeetingAttendees",
                columns: table => new
                {
                    MeetingAttendeeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    ExternalName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Attended = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingAttendees", x => x.MeetingAttendeeId);
                    table.ForeignKey(
                        name: "FK_MeetingAttendees_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeetingAttendees_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "MeetingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementApprovals",
                columns: table => new
                {
                    ProcurementApprovalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: false),
                    ApproverName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovalDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementApprovals", x => x.ProcurementApprovalId);
                    table.ForeignKey(
                        name: "FK_ProcurementApprovals_ProcurementPackages_ProcurementPackageId",
                        column: x => x.ProcurementPackageId,
                        principalTable: "ProcurementPackages",
                        principalColumn: "ProcurementPackageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VDRImportRows",
                columns: table => new
                {
                    VDRImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VDRImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VendorNameRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    CreatedVDRItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VDRImportRows", x => x.VDRImportRowId);
                    table.ForeignKey(
                        name: "FK_VDRImportRows_VDRImportBatches_VDRImportBatchId",
                        column: x => x.VDRImportBatchId,
                        principalTable: "VDRImportBatches",
                        principalColumn: "VDRImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ClauseReference = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocuments", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_ContractDocuments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    PoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PoNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.PoId);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStages",
                columns: table => new
                {
                    WorkflowStageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowVersionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StageOrder = table.Column<int>(type: "int", nullable: false),
                    StageType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResponsibleRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovalStrategy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumApprovalCount = table.Column<int>(type: "int", nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    IsSkippable = table.Column<bool>(type: "bit", nullable: false),
                    IsDelegatable = table.Column<bool>(type: "bit", nullable: false),
                    IsEscalatable = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutHours = table.Column<int>(type: "int", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStages", x => x.WorkflowStageId);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_WorkflowVersions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalTable: "WorkflowVersions",
                        principalColumn: "WorkflowVersionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentImportRows",
                columns: table => new
                {
                    EquipmentImportRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludeInCommit = table.Column<bool>(type: "bit", nullable: false),
                    ValidationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidationMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsUpdate = table.Column<bool>(type: "bit", nullable: false),
                    CreatedEquipmentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentImportRows", x => x.EquipmentImportRowId);
                    table.ForeignKey(
                        name: "FK_EquipmentImportRows_EquipmentImportBatches_EquipmentImportBatchId",
                        column: x => x.EquipmentImportBatchId,
                        principalTable: "EquipmentImportBatches",
                        principalColumn: "EquipmentImportBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    TeamMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.TeamMemberId);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembers_MaintenanceRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "MaintenanceRoles",
                        principalColumn: "MaintenanceRoleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransmittalItems",
                columns: table => new
                {
                    TransmittalItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransmittalId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransmittalItems", x => x.TransmittalItemId);
                    table.ForeignKey(
                        name: "FK_TransmittalItems_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransmittalItems_Transmittals_TransmittalId",
                        column: x => x.TransmittalId,
                        principalTable: "Transmittals",
                        principalColumn: "TransmittalId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstances",
                columns: table => new
                {
                    WorkflowInstanceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowVersionId = table.Column<int>(type: "int", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessObjectType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessObjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    CurrentStageId = table.Column<int>(type: "int", nullable: true),
                    CurrentOwnerId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitiatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstances", x => x.WorkflowInstanceId);
                    table.ForeignKey(
                        name: "FK_WorkflowInstances_WorkflowStages_CurrentStageId",
                        column: x => x.CurrentStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "WorkflowStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowInstances_WorkflowVersions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalTable: "WorkflowVersions",
                        principalColumn: "WorkflowVersionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitions",
                columns: table => new
                {
                    WorkflowTransitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowVersionId = table.Column<int>(type: "int", nullable: false),
                    FromStageId = table.Column<int>(type: "int", nullable: true),
                    ToStageId = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConditionExpression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitions", x => x.WorkflowTransitionId);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitions_WorkflowStages_FromStageId",
                        column: x => x.FromStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "WorkflowStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitions_WorkflowStages_ToStageId",
                        column: x => x.ToStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "WorkflowStageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitions_WorkflowVersions_WorkflowVersionId",
                        column: x => x.WorkflowVersionId,
                        principalTable: "WorkflowVersions",
                        principalColumn: "WorkflowVersionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowHistories",
                columns: table => new
                {
                    WorkflowHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceId = table.Column<int>(type: "int", nullable: false),
                    FromStageId = table.Column<int>(type: "int", nullable: true),
                    ToStageId = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RuleApplied = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Browser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowHistories", x => x.WorkflowHistoryId);
                    table.ForeignKey(
                        name: "FK_WorkflowHistories_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "WorkflowInstances",
                        principalColumn: "WorkflowInstanceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstanceStages",
                columns: table => new
                {
                    WorkflowInstanceStageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowInstanceId = table.Column<int>(type: "int", nullable: false),
                    WorkflowStageId = table.Column<int>(type: "int", nullable: false),
                    AssignedToEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AssignedToRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstanceStages", x => x.WorkflowInstanceStageId);
                    table.ForeignKey(
                        name: "FK_WorkflowInstanceStages_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "WorkflowInstances",
                        principalColumn: "WorkflowInstanceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowInstanceStages_WorkflowStages_WorkflowStageId",
                        column: x => x.WorkflowStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "WorkflowStageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIDocumentClassifications",
                columns: table => new
                {
                    AIDocumentClassificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttachmentId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    PredictedCategory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedDocumentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedDisciplineCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedProjectHint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedArea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedSystem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedSubsystem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedRevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedIssuePurpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedVendorName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedMRNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PredictedEquipmentTagNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfidenceScore = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedTargetModule = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIDocumentClassifications", x => x.AIDocumentClassificationId);
                    table.ForeignKey(
                        name: "FK_AIDocumentClassifications_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AIExtractedSpecifications",
                columns: table => new
                {
                    ExtractedSpecificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttachmentId = table.Column<int>(type: "int", nullable: false),
                    RelatedTagNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FieldName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confidence = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIExtractedSpecifications", x => x.ExtractedSpecificationId);
                    table.ForeignKey(
                        name: "FK_AIExtractedSpecifications_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIExtractedTags",
                columns: table => new
                {
                    ExtractedTagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttachmentId = table.Column<int>(type: "int", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confidence = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    LinkedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    CreatedTagId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIExtractedTags", x => x.ExtractedTagId);
                    table.ForeignKey(
                        name: "FK_AIExtractedTags_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alarms",
                columns: table => new
                {
                    AlarmId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    AlarmTag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AlarmType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetPoint = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alarms", x => x.AlarmId);
                });

            migrationBuilder.CreateTable(
                name: "CauseEffects",
                columns: table => new
                {
                    CauseEffectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlarmId = table.Column<int>(type: "int", nullable: false),
                    Cause = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TripSignal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CauseEffects", x => x.CauseEffectId);
                    table.ForeignKey(
                        name: "FK_CauseEffects_Alarms_AlarmId",
                        column: x => x.AlarmId,
                        principalTable: "Alarms",
                        principalColumn: "AlarmId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    AssetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    AssetNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FunctionalLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Criticality = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaintenanceClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommissioningDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.AssetId);
                });

            migrationBuilder.CreateTable(
                name: "ControlLoops",
                columns: table => new
                {
                    LoopId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoopNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessArea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ControllerType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ControllerSystem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InstrumentId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlLoops", x => x.LoopId);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    UploadedById = table.Column<int>(type: "int", nullable: true),
                    IntakeProjectId = table.Column<int>(type: "int", nullable: true),
                    IntakeStage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RoutedModule = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RoutedEntityId = table.Column<int>(type: "int", nullable: true),
                    LetterId = table.Column<int>(type: "int", nullable: true),
                    TechnicalQueryId = table.Column<int>(type: "int", nullable: true),
                    MOCId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileExtension = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AIStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AISummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AIKeywords = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AITags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AIConfidence = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: true),
                    AIError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AIProcessedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UploadDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_DocumentAttachments_Employees_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentAttachments_Projects_IntakeProjectId",
                        column: x => x.IntakeProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcurementCommunications",
                columns: table => new
                {
                    ProcurementCommunicationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    RaisedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RaisedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttachmentId = table.Column<int>(type: "int", nullable: true),
                    ActionStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementCommunications", x => x.ProcurementCommunicationId);
                    table.ForeignKey(
                        name: "FK_ProcurementCommunications_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcurementCommunications_ProcurementPackages_ProcurementPackageId",
                        column: x => x.ProcurementPackageId,
                        principalTable: "ProcurementPackages",
                        principalColumn: "ProcurementPackageId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcurementCommunications_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentComments",
                columns: table => new
                {
                    DocumentCommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VDRSubmissionId = table.Column<int>(type: "int", nullable: true),
                    CommentNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sheet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Zone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommentText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RaisedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RaisedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActionStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttachmentId = table.Column<int>(type: "int", nullable: true),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplyAttachmentId = table.Column<int>(type: "int", nullable: true),
                    ClosedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    PinX = table.Column<double>(type: "float", nullable: true),
                    PinY = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentComments", x => x.DocumentCommentId);
                    table.ForeignKey(
                        name: "FK_DocumentComments_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentComments_DocumentAttachments_ReplyAttachmentId",
                        column: x => x.ReplyAttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentComments_DocumentComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "DocumentComments",
                        principalColumn: "DocumentCommentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentComments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRevisions",
                columns: table => new
                {
                    RevisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PreviousRevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuePurpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevisionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreparedById = table.Column<int>(type: "int", nullable: true),
                    CheckedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededRevisionId = table.Column<int>(type: "int", nullable: true),
                    AIRevisionSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevisions", x => x.RevisionId);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_DocumentRevisions_SupersededRevisionId",
                        column: x => x.SupersededRevisionId,
                        principalTable: "DocumentRevisions",
                        principalColumn: "RevisionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Employees_CheckedById",
                        column: x => x.CheckedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Employees_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Employees_PreparedById",
                        column: x => x.PreparedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Employees_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTagLinks",
                columns: table => new
                {
                    DocumentTagLinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    EquipmentTagId = table.Column<int>(type: "int", nullable: false),
                    IsAIProposed = table.Column<bool>(type: "bit", nullable: false),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTagLinks", x => x.DocumentTagLinkId);
                    table.ForeignKey(
                        name: "FK_DocumentTagLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentWorkflows",
                columns: table => new
                {
                    WorkflowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActionById = table.Column<int>(type: "int", nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Browser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentWorkflows", x => x.WorkflowId);
                    table.ForeignKey(
                        name: "FK_DocumentWorkflows_Employees_ActionById",
                        column: x => x.ActionById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                });

            migrationBuilder.CreateTable(
                name: "ElectricalEquipments",
                columns: table => new
                {
                    ElectricalEquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PowerKW = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Voltage = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RatedCurrent = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    SupplySource = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectricalEquipments", x => x.ElectricalEquipmentId);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    RelatedMRId = table.Column<int>(type: "int", nullable: true),
                    IssuePurpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreparedById = table.Column<int>(type: "int", nullable: true),
                    CheckedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    CheckedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientApprovalStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringDocuments", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Employees_CheckedById",
                        column: x => x.CheckedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Employees_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId");
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Employees_PreparedById",
                        column: x => x.PreparedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringDocuments_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRequisitions",
                columns: table => new
                {
                    MRId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MRNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: true),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    EquipmentTagNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponsibleEngineerId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedDocumentId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRequisitions", x => x.MRId);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitions_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitions_Employees_ResponsibleEngineerId",
                        column: x => x.ResponsibleEngineerId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitions_EngineeringDocuments_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "EngineeringDocuments",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitions_ProcurementPackages_ProcurementPackageId",
                        column: x => x.ProcurementPackageId,
                        principalTable: "ProcurementPackages",
                        principalColumn: "ProcurementPackageId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    EquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PIDNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedMRId = table.Column<int>(type: "int", nullable: true),
                    MRNumberRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PONumberRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.EquipmentId);
                    table.ForeignKey(
                        name: "FK_Equipments_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Equipments_MaterialRequisitions_RelatedMRId",
                        column: x => x.RelatedMRId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId");
                    table.ForeignKey(
                        name: "FK_Equipments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Letters",
                columns: table => new
                {
                    LetterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LetterNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LetterDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sender = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Receiver = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiverDepartmentId = table.Column<int>(type: "int", nullable: true),
                    ReceiverEmployeeId = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfidentialityLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: true),
                    DisciplineId = table.Column<int>(type: "int", nullable: true),
                    RelatedDocumentId = table.Column<int>(type: "int", nullable: true),
                    RelatedMRId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RegisteredById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ParentLetterId = table.Column<int>(type: "int", nullable: true),
                    IssuedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Letters", x => x.LetterId);
                    table.ForeignKey(
                        name: "FK_Letters_Departments_ReceiverDepartmentId",
                        column: x => x.ReceiverDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_Employees_ReceiverEmployeeId",
                        column: x => x.ReceiverEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_Letters_ParentLetterId",
                        column: x => x.ParentLetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_MaterialRequisitions_RelatedMRId",
                        column: x => x.RelatedMRId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Letters_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRequisitionItems",
                columns: table => new
                {
                    MRItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MRId = table.Column<int>(type: "int", nullable: false),
                    ItemDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRequisitionItems", x => x.MRItemId);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitionItems_MaterialRequisitions_MRId",
                        column: x => x.MRId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRequisitionQuotations",
                columns: table => new
                {
                    MRQId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MRQNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MRId = table.Column<int>(type: "int", nullable: false),
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: true),
                    VendorName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InquiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuotedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QuotationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TechnicalEvaluation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommercialEvaluation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRequisitionQuotations", x => x.MRQId);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitionQuotations_MaterialRequisitions_MRId",
                        column: x => x.MRId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialRequisitionQuotations_ProcurementPackages_ProcurementPackageId",
                        column: x => x.ProcurementPackageId,
                        principalTable: "ProcurementPackages",
                        principalColumn: "ProcurementPackageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentCriticalities",
                columns: table => new
                {
                    EquipmentCriticalityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    CriticalityClass = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SafetyImpact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductionImpact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnvironmentImpact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCriticalities", x => x.EquipmentCriticalityId);
                    table.ForeignKey(
                        name: "FK_EquipmentCriticalities_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentDatasheets",
                columns: table => new
                {
                    DatasheetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisionNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DesignPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OperatingPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OperatingTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Specification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentDatasheets", x => x.DatasheetId);
                    table.ForeignKey(
                        name: "FK_EquipmentDatasheets_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentNozzles",
                columns: table => new
                {
                    EquipmentNozzleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    NozzleTag = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rating = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Orientation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentNozzles", x => x.EquipmentNozzleId);
                    table.ForeignKey(
                        name: "FK_EquipmentNozzles_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentTags",
                columns: table => new
                {
                    TagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Service = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoopNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentTags", x => x.TagId);
                    table.ForeignKey(
                        name: "FK_EquipmentTags_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InspectionTestPlans",
                columns: table => new
                {
                    InspectionTestPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ITPNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    QCDisciplineId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ReferenceCodeStandard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_InspectionTestPlans", x => x.InspectionTestPlanId);
                    table.ForeignKey(
                        name: "FK_InspectionTestPlans_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionTestPlans_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionTestPlans_QCDisciplines_QCDisciplineId",
                        column: x => x.QCDisciplineId,
                        principalTable: "QCDisciplines",
                        principalColumn: "QCDisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    InstrumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstrumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RangeMin = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RangeMax = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignalType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CalibrationInterval = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.InstrumentId);
                    table.ForeignKey(
                        name: "FK_Instruments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceStrategies",
                columns: table => new
                {
                    MaintenanceStrategyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    StrategyType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureModeCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsequenceCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecommendedTriggerType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStrategies", x => x.MaintenanceStrategyId);
                    table.ForeignKey(
                        name: "FK_MaintenanceStrategies_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MechanicalEquipments",
                columns: table => new
                {
                    MechanicalEquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DesignPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OperatingPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OperatingTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MechanicalEquipments", x => x.MechanicalEquipmentId);
                    table.ForeignKey(
                        name: "FK_MechanicalEquipments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId");
                });

            migrationBuilder.CreateTable(
                name: "MOCs",
                columns: table => new
                {
                    MOCId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MOCNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemporaryExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RelatedDocumentId = table.Column<int>(type: "int", nullable: true),
                    RelatedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: true),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RiskAssessmentText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiskAssessedById = table.Column<int>(type: "int", nullable: true),
                    RiskAssessedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImplementationPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImplementedById = table.Column<int>(type: "int", nullable: true),
                    ImplementedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentsUpdated = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedDocumentsNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TrainingRequired = table.Column<bool>(type: "bit", nullable: false),
                    TrainingCompleted = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClosedById = table.Column<int>(type: "int", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MOCs", x => x.MOCId);
                    table.ForeignKey(
                        name: "FK_MOCs_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOCs_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOCs_Equipments_RelatedEquipmentId",
                        column: x => x.RelatedEquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MOCs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TechnicalQueries",
                columns: table => new
                {
                    TechnicalQueryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueryNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QueryType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedDocumentId = table.Column<int>(type: "int", nullable: true),
                    RelatedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    RaisedById = table.Column<int>(type: "int", nullable: true),
                    RaisedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequiredResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedById = table.Column<int>(type: "int", nullable: true),
                    RespondedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalQueries", x => x.TechnicalQueryId);
                    table.ForeignKey(
                        name: "FK_TechnicalQueries_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TechnicalQueries_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TechnicalQueries_Equipments_RelatedEquipmentId",
                        column: x => x.RelatedEquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TechnicalQueries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VDRItems",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    DisciplineCodeRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsMultiDiscipline = table.Column<bool>(type: "bit", nullable: false),
                    MRNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MRId = table.Column<int>(type: "int", nullable: true),
                    CommercialExpeditorName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EngineeringExpeditorName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    FolderNoOnFtp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PoId = table.Column<int>(type: "int", nullable: true),
                    PackageDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentTypeCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsiblePartyCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentClass = table.Column<int>(type: "int", nullable: true),
                    DocumentClassRaw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WrapUpStatusImported = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlannedFirstIssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkflowStatus = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VDRItems", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_VDRItems_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VDRItems_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRItems_MaterialRequisitions_MRId",
                        column: x => x.MRId,
                        principalTable: "MaterialRequisitions",
                        principalColumn: "MRId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRItems_PurchaseOrders_PoId",
                        column: x => x.PoId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "PoId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRItems_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LetterRecipients",
                columns: table => new
                {
                    LetterRecipientId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    ExternalName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterRecipients", x => x.LetterRecipientId);
                    table.ForeignKey(
                        name: "FK_LetterRecipients_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterRecipients_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterRecipients_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterReferrals",
                columns: table => new
                {
                    LetterReferralId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    ReferredToEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReferredToDepartmentId = table.Column<int>(type: "int", nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferredByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferredDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResponseNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterReferrals", x => x.LetterReferralId);
                    table.ForeignKey(
                        name: "FK_LetterReferrals_Departments_ReferredToDepartmentId",
                        column: x => x.ReferredToDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterReferrals_Employees_ReferredToEmployeeId",
                        column: x => x.ReferredToEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterReferrals_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterStars",
                columns: table => new
                {
                    LetterStarId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterStars", x => x.LetterStarId);
                    table.ForeignKey(
                        name: "FK_LetterStars_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterTags",
                columns: table => new
                {
                    LetterTagId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterTags", x => x.LetterTagId);
                    table.ForeignKey(
                        name: "FK_LetterTags_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LetterTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ITPLineItems",
                columns: table => new
                {
                    ITPLineItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionTestPlanId = table.Column<int>(type: "int", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    Activity = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    QCDisciplineId = table.Column<int>(type: "int", nullable: false),
                    InspectionCategory = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    AcceptanceCriteria = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceCodeStandard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsibleParty = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NotifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotifiedById = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITPLineItems", x => x.ITPLineItemId);
                    table.ForeignKey(
                        name: "FK_ITPLineItems_InspectionTestPlans_InspectionTestPlanId",
                        column: x => x.InspectionTestPlanId,
                        principalTable: "InspectionTestPlans",
                        principalColumn: "InspectionTestPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ITPLineItems_QCDisciplines_QCDisciplineId",
                        column: x => x.QCDisciplineId,
                        principalTable: "QCDisciplines",
                        principalColumn: "QCDisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IOPoints",
                columns: table => new
                {
                    IOPointId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IOType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PLCName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Slot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IOPoints", x => x.IOPointId);
                    table.ForeignKey(
                        name: "FK_IOPoints_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                columns: table => new
                {
                    MaintenancePlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceStrategyId = table.Column<int>(type: "int", nullable: false),
                    PlanNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: true),
                    TriggerType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeterType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MeterInterval = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LastExecutionMeterReading = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NextDueMeterReading = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LastExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_MaintenancePlans", x => x.MaintenancePlanId);
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_MaintenanceStrategies_MaintenanceStrategyId",
                        column: x => x.MaintenanceStrategyId,
                        principalTable: "MaintenanceStrategies",
                        principalColumn: "MaintenanceStrategyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RotatingEquipments",
                columns: table => new
                {
                    RotatingEquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MechanicalEquipmentId = table.Column<int>(type: "int", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FlowRate = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Head = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    SpeedRPM = table.Column<int>(type: "int", nullable: true),
                    PowerKW = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DriverType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BearingType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LubricationType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotatingEquipments", x => x.RotatingEquipmentId);
                    table.ForeignKey(
                        name: "FK_RotatingEquipments_MechanicalEquipments_MechanicalEquipmentId",
                        column: x => x.MechanicalEquipmentId,
                        principalTable: "MechanicalEquipments",
                        principalColumn: "MechanicalEquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaticEquipments",
                columns: table => new
                {
                    StaticEquipmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MechanicalEquipmentId = table.Column<int>(type: "int", nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignPressure = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DesignTemperature = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Orientation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CodeStandard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaticEquipments", x => x.StaticEquipmentId);
                    table.ForeignKey(
                        name: "FK_StaticEquipments_MechanicalEquipments_MechanicalEquipmentId",
                        column: x => x.MechanicalEquipmentId,
                        principalTable: "MechanicalEquipments",
                        principalColumn: "MechanicalEquipmentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VDRSubmissions",
                columns: table => new
                {
                    VDRSubmissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VDRItemId = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    VendorTransmittalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientTransmittalNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientTransmittalId = table.Column<int>(type: "int", nullable: true),
                    VendorActualIssuedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurposeOfIssueCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientCommentSheetNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientCommentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewCategory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplySheetNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplySheetDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttachmentId = table.Column<int>(type: "int", nullable: true),
                    CommentSheetAttachmentId = table.Column<int>(type: "int", nullable: true),
                    ReplySheetAttachmentId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VDRSubmissions", x => x.VDRSubmissionId);
                    table.ForeignKey(
                        name: "FK_VDRSubmissions_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRSubmissions_DocumentAttachments_CommentSheetAttachmentId",
                        column: x => x.CommentSheetAttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRSubmissions_DocumentAttachments_ReplySheetAttachmentId",
                        column: x => x.ReplySheetAttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRSubmissions_Transmittals_ClientTransmittalId",
                        column: x => x.ClientTransmittalId,
                        principalTable: "Transmittals",
                        principalColumn: "TransmittalId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VDRSubmissions_VDRItems_VDRItemId",
                        column: x => x.VDRItemId,
                        principalTable: "VDRItems",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InspectionRecords",
                columns: table => new
                {
                    InspectionRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    ITPLineItemId = table.Column<int>(type: "int", nullable: true),
                    QCDisciplineId = table.Column<int>(type: "int", nullable: true),
                    InspectionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InspectionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Inspector = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextInspectionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionRecords", x => x.InspectionRecordId);
                    table.ForeignKey(
                        name: "FK_InspectionRecords_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionRecords_ITPLineItems_ITPLineItemId",
                        column: x => x.ITPLineItemId,
                        principalTable: "ITPLineItems",
                        principalColumn: "ITPLineItemId");
                    table.ForeignKey(
                        name: "FK_InspectionRecords_QCDisciplines_QCDisciplineId",
                        column: x => x.QCDisciplineId,
                        principalTable: "QCDisciplines",
                        principalColumn: "QCDisciplineId");
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    WorkOrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    MaintenancePlanId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkOrderType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedTo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedTeamId = table.Column<int>(type: "int", nullable: true),
                    AssignedEmployeeId = table.Column<int>(type: "int", nullable: true),
                    FailureModeCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureCauseCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DowntimeHours = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.WorkOrderId);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Employees_AssignedEmployeeId",
                        column: x => x.AssignedEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_MaintenancePlans_MaintenancePlanId",
                        column: x => x.MaintenancePlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "MaintenancePlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Teams_AssignedTeamId",
                        column: x => x.AssignedTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionChecklists",
                columns: table => new
                {
                    InspectionChecklistId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionRecordId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Requirement = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionChecklists", x => x.InspectionChecklistId);
                    table.ForeignKey(
                        name: "FK_InspectionChecklists_InspectionRecords_InspectionRecordId",
                        column: x => x.InspectionRecordId,
                        principalTable: "InspectionRecords",
                        principalColumn: "InspectionRecordId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NonConformanceReports",
                columns: table => new
                {
                    NonConformanceReportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NCRNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    QCDisciplineId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    ITPLineItemId = table.Column<int>(type: "int", nullable: true),
                    InspectionRecordId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Disposition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreventiveAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RaisedById = table.Column<int>(type: "int", nullable: true),
                    RaisedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispositionedById = table.Column<int>(type: "int", nullable: true),
                    DispositionedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedById = table.Column<int>(type: "int", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonConformanceReports", x => x.NonConformanceReportId);
                    table.ForeignKey(
                        name: "FK_NonConformanceReports_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonConformanceReports_ITPLineItems_ITPLineItemId",
                        column: x => x.ITPLineItemId,
                        principalTable: "ITPLineItems",
                        principalColumn: "ITPLineItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonConformanceReports_InspectionRecords_InspectionRecordId",
                        column: x => x.InspectionRecordId,
                        principalTable: "InspectionRecords",
                        principalColumn: "InspectionRecordId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonConformanceReports_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NonConformanceReports_QCDisciplines_QCDisciplineId",
                        column: x => x.QCDisciplineId,
                        principalTable: "QCDisciplines",
                        principalColumn: "QCDisciplineId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FailureRecords",
                columns: table => new
                {
                    FailureRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    FailureDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailureMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureEffect = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DowntimeHours = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailureRecords", x => x.FailureRecordId);
                    table.ForeignKey(
                        name: "FK_FailureRecords_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FailureRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceDocuments",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceDocuments", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_MaintenanceDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceDocuments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceDocuments_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceHistories",
                columns: table => new
                {
                    MaintenanceHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Finding = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionTaken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Technician = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationHours = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceHistories", x => x.MaintenanceHistoryId);
                    table.ForeignKey(
                        name: "FK_MaintenanceHistories_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceHistories_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceSpareUsages",
                columns: table => new
                {
                    MaintenanceSpareUsageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    SparePartId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    UsageDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceSpareUsages", x => x.MaintenanceSpareUsageId);
                    table.ForeignKey(
                        name: "FK_MaintenanceSpareUsages_SpareParts_SparePartId",
                        column: x => x.SparePartId,
                        principalTable: "SpareParts",
                        principalColumn: "SparePartId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceSpareUsages_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderDisciplineFindings",
                columns: table => new
                {
                    WorkOrderDisciplineFindingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Findings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureModeCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureCauseCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedById = table.Column<int>(type: "int", nullable: true),
                    RespondedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderDisciplineFindings", x => x.WorkOrderDisciplineFindingId);
                    table.ForeignKey(
                        name: "FK_WorkOrderDisciplineFindings_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderDisciplineFindings_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderDisciplineReferrals",
                columns: table => new
                {
                    WorkOrderDisciplineReferralId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    CycleNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderDisciplineReferrals", x => x.WorkOrderDisciplineReferralId);
                    table.ForeignKey(
                        name: "FK_WorkOrderDisciplineReferrals_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "DisciplineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderDisciplineReferrals_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderLabors",
                columns: table => new
                {
                    WorkOrderLaborId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RegularHours = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OvertimeRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LaborCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_WorkOrderLabors", x => x.WorkOrderLaborId);
                    table.ForeignKey(
                        name: "FK_WorkOrderLabors_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderLabors_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderLabors_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "WorkOrderId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Disciplines",
                columns: new[] { "DisciplineId", "Code", "CreatedDate", "Description", "ImportAliases", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "PROC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Process Engineering", "PR,PRO,PROCESS,PROCS", true, "Process" },
                    { 2, "MECH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Mechanical Engineering", "ME,MEC,MECHANICAL,ROTATING,STATIC", true, "Mechanical" },
                    { 3, "ELEC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Electrical Engineering", "EL,ELE,ELECTRICAL,PWR,POWER", true, "Electrical" },
                    { 4, "INST", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrumentation and Control Engineering", "IN,INS,IC,I&C,INSTRUMENT,CONTROL,INSTRUMENTATION", true, "Instrumentation" },
                    { 5, "PIPE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Piping Engineering", "PI,PIP,PL,PIPING,PIPELINE", true, "Piping" },
                    { 6, "CIVL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Civil Engineering", "CI,CIV,ST,STR,CIVIL,STRUCTURAL,CS", true, "Civil" }
                });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "ProjectId", "ClientName", "CreatedDate", "EndDate", "IsActive", "Location", "ProjectCode", "ProjectName", "StartDate", "Status" },
                values: new object[] { 1, "Petrochemical", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, true, "Assaluyeh", "PEMS-001", "Polymer Plant Development", null, "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_AIDocumentClassifications_AttachmentId",
                table: "AIDocumentClassifications",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIDocumentClassifications_DocumentId",
                table: "AIDocumentClassifications",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedSpecifications_AttachmentId",
                table: "AIExtractedSpecifications",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedSpecifications_ReviewedById",
                table: "AIExtractedSpecifications",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedTags_AttachmentId",
                table: "AIExtractedTags",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedTags_CreatedTagId",
                table: "AIExtractedTags",
                column: "CreatedTagId");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedTags_LinkedEquipmentId",
                table: "AIExtractedTags",
                column: "LinkedEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AIExtractedTags_ReviewedById",
                table: "AIExtractedTags",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_Alarms_InstrumentId",
                table: "Alarms",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_EquipmentId",
                table: "Assets",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CableConnections_CableId",
                table: "CableConnections",
                column: "CableId");

            migrationBuilder.CreateIndex(
                name: "IX_CauseEffects_AlarmId",
                table: "CauseEffects",
                column: "AlarmId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDocuments_ContractId",
                table: "ContractDocuments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ProjectId",
                table: "Contracts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_VendorId",
                table: "Contracts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlLoops_InstrumentId",
                table: "ControlLoops",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTasks_DisciplineId",
                table: "DisciplineTasks",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTasks_ProjectId",
                table: "DisciplineTasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineTasks_ResponsibleEmployeeId",
                table: "DisciplineTasks",
                column: "ResponsibleEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_DocumentId",
                table: "DocumentAttachments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_IntakeProjectId",
                table: "DocumentAttachments",
                column: "IntakeProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_LetterId",
                table: "DocumentAttachments",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_MOCId",
                table: "DocumentAttachments",
                column: "MOCId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_TechnicalQueryId",
                table: "DocumentAttachments",
                column: "TechnicalQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_UploadedById",
                table: "DocumentAttachments",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_AttachmentId",
                table: "DocumentComments",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_DocumentId",
                table: "DocumentComments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_ParentCommentId",
                table: "DocumentComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_ReplyAttachmentId",
                table: "DocumentComments",
                column: "ReplyAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_VDRSubmissionId",
                table: "DocumentComments",
                column: "VDRSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIdcReferrals_DisciplineId",
                table: "DocumentIdcReferrals",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIdcReferrals_DocumentId_DisciplineId",
                table: "DocumentIdcReferrals",
                columns: new[] { "DocumentId", "DisciplineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_ApprovedById",
                table: "DocumentRevisions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_CheckedById",
                table: "DocumentRevisions",
                column: "CheckedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_CreatedById",
                table: "DocumentRevisions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_DocumentId_RevisionNo",
                table: "DocumentRevisions",
                columns: new[] { "DocumentId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_PreparedById",
                table: "DocumentRevisions",
                column: "PreparedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_ReviewerId",
                table: "DocumentRevisions",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_SupersededRevisionId",
                table: "DocumentRevisions",
                column: "SupersededRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DisciplineId",
                table: "Documents",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IsActive_Status",
                table: "Documents",
                columns: new[] { "IsActive", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProjectId",
                table: "Documents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTagLinks_DocumentId",
                table: "DocumentTagLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTagLinks_EquipmentTagId",
                table: "DocumentTagLinks",
                column: "EquipmentTagId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentWorkflows_ActionById",
                table: "DocumentWorkflows",
                column: "ActionById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentWorkflows_DocumentId",
                table: "DocumentWorkflows",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricalEquipments_EquipmentId",
                table: "ElectricalEquipments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLaborRates_EmployeeId",
                table: "EmployeeLaborRates",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DisciplineId",
                table: "Employees",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_PersonnelCode",
                table: "Employees",
                column: "PersonnelCode",
                unique: true,
                filter: "[PersonnelCode] IS NOT NULL AND [PersonnelCode] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_ApprovedById",
                table: "EngineeringDocuments",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_CheckedById",
                table: "EngineeringDocuments",
                column: "CheckedById");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_ContractId",
                table: "EngineeringDocuments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_DeletedById",
                table: "EngineeringDocuments",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_EquipmentId",
                table: "EngineeringDocuments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_PreparedById",
                table: "EngineeringDocuments",
                column: "PreparedById");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_RelatedMRId",
                table: "EngineeringDocuments",
                column: "RelatedMRId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringDocuments_VendorId",
                table: "EngineeringDocuments",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCriticalities_EquipmentId",
                table: "EquipmentCriticalities",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDatasheets_EquipmentId",
                table: "EquipmentDatasheets",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentImportBatches_ImportedById",
                table: "EquipmentImportBatches",
                column: "ImportedById");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentImportBatches_ProjectId",
                table: "EquipmentImportBatches",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentImportRows_EquipmentImportBatchId",
                table: "EquipmentImportRows",
                column: "EquipmentImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentNozzles_EquipmentId",
                table: "EquipmentNozzles",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_DisciplineId",
                table: "Equipments",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_ProjectId",
                table: "Equipments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_RelatedMRId",
                table: "Equipments",
                column: "RelatedMRId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentTags_EquipmentId",
                table: "EquipmentTags",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_EquipmentId",
                table: "FailureRecords",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FailureRecords_WorkOrderId",
                table: "FailureRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalLocations_ParentId",
                table: "FunctionalLocations",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionChecklists_InspectionRecordId",
                table: "InspectionChecklists",
                column: "InspectionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRecords_EquipmentId",
                table: "InspectionRecords",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRecords_ITPLineItemId",
                table: "InspectionRecords",
                column: "ITPLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRecords_QCDisciplineId",
                table: "InspectionRecords",
                column: "QCDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTestPlans_EquipmentId",
                table: "InspectionTestPlans",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTestPlans_ProjectId_ITPNumber_Revision",
                table: "InspectionTestPlans",
                columns: new[] { "ProjectId", "ITPNumber", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTestPlans_QCDisciplineId",
                table: "InspectionTestPlans",
                column: "QCDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_EquipmentId",
                table: "Instruments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_IOPoints_InstrumentId",
                table: "IOPoints",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ITPLineItems_InspectionTestPlanId_SequenceNo",
                table: "ITPLineItems",
                columns: new[] { "InspectionTestPlanId", "SequenceNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ITPLineItems_QCDisciplineId",
                table: "ITPLineItems",
                column: "QCDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseEntries_SourceType_SourceId",
                table: "KnowledgeBaseEntries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterRecipients_DepartmentId",
                table: "LetterRecipients",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterRecipients_EmployeeId",
                table: "LetterRecipients",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterRecipients_LetterId",
                table: "LetterRecipients",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterReferrals_LetterId",
                table: "LetterReferrals",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterReferrals_ReferredToDepartmentId",
                table: "LetterReferrals",
                column: "ReferredToDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterReferrals_ReferredToEmployeeId",
                table: "LetterReferrals",
                column: "ReferredToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_DisciplineId",
                table: "Letters",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_IsActive_LetterDate",
                table: "Letters",
                columns: new[] { "IsActive", "LetterDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Letters_LetterNumber",
                table: "Letters",
                column: "LetterNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_LetterType",
                table: "Letters",
                column: "LetterType");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ParentLetterId",
                table: "Letters",
                column: "ParentLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ProjectId",
                table: "Letters",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ReceiverDepartmentId",
                table: "Letters",
                column: "ReceiverDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_ReceiverEmployeeId",
                table: "Letters",
                column: "ReceiverEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_RelatedDocumentId",
                table: "Letters",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_RelatedMRId",
                table: "Letters",
                column: "RelatedMRId");

            migrationBuilder.CreateIndex(
                name: "IX_Letters_Status",
                table: "Letters",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LetterStars_LetterId_ApplicationUserId",
                table: "LetterStars",
                columns: new[] { "LetterId", "ApplicationUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterTags_LetterId_TagId",
                table: "LetterTags",
                columns: new[] { "LetterId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterTags_TagId",
                table: "LetterTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDocuments_EquipmentId",
                table: "MaintenanceDocuments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDocuments_WorkOrderId",
                table: "MaintenanceDocuments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceHistories_EquipmentId",
                table: "MaintenanceHistories",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceHistories_WorkOrderId",
                table: "MaintenanceHistories",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_EquipmentId",
                table: "MaintenancePlans",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_MaintenanceStrategyId",
                table: "MaintenancePlans",
                column: "MaintenanceStrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRoles_DisciplineId_Name",
                table: "MaintenanceRoles",
                columns: new[] { "DisciplineId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSpareUsages_SparePartId",
                table: "MaintenanceSpareUsages",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSpareUsages_WorkOrderId",
                table: "MaintenanceSpareUsages",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_EquipmentId",
                table: "MaintenanceStrategies",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionItems_MRId",
                table: "MaterialRequisitionItems",
                column: "MRId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionQuotations_MRId",
                table: "MaterialRequisitionQuotations",
                column: "MRId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitionQuotations_ProcurementPackageId",
                table: "MaterialRequisitionQuotations",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_DisciplineId",
                table: "MaterialRequisitions",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_ProcurementPackageId",
                table: "MaterialRequisitions",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_ProjectId",
                table: "MaterialRequisitions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_RelatedDocumentId",
                table: "MaterialRequisitions",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRequisitions_ResponsibleEngineerId",
                table: "MaterialRequisitions",
                column: "ResponsibleEngineerId");

            migrationBuilder.CreateIndex(
                name: "IX_MDRImportBatches_ProjectId",
                table: "MDRImportBatches",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MDRImportRows_MDRImportBatchId",
                table: "MDRImportRows",
                column: "MDRImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MechanicalEquipments_EquipmentId",
                table: "MechanicalEquipments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAttendees_EmployeeId",
                table: "MeetingAttendees",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAttendees_MeetingId",
                table: "MeetingAttendees",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_ProjectId",
                table: "Meetings",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MOCs_DisciplineId",
                table: "MOCs",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_MOCs_ProjectId_MOCNumber",
                table: "MOCs",
                columns: new[] { "ProjectId", "MOCNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MOCs_RelatedDocumentId",
                table: "MOCs",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MOCs_RelatedEquipmentId",
                table: "MOCs",
                column: "RelatedEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformanceReports_EquipmentId",
                table: "NonConformanceReports",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformanceReports_InspectionRecordId",
                table: "NonConformanceReports",
                column: "InspectionRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformanceReports_ITPLineItemId",
                table: "NonConformanceReports",
                column: "ITPLineItemId");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformanceReports_ProjectId_NCRNumber",
                table: "NonConformanceReports",
                columns: new[] { "ProjectId", "NCRNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonConformanceReports_QCDisciplineId",
                table: "NonConformanceReports",
                column: "QCDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementApprovals_ProcurementPackageId",
                table: "ProcurementApprovals",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCommunications_AttachmentId",
                table: "ProcurementCommunications",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCommunications_ProcurementPackageId",
                table: "ProcurementCommunications",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementCommunications_VendorId",
                table: "ProcurementCommunications",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementPackages_DisciplineId",
                table: "ProcurementPackages",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementPackages_ProjectId",
                table: "ProcurementPackages",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ContractId",
                table: "PurchaseOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ProjectId",
                table: "PurchaseOrders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_VendorId",
                table: "PurchaseOrders",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_QCDisciplines_Code",
                table: "QCDisciplines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleName_ModuleName",
                table: "RolePermissions",
                columns: new[] { "RoleName", "ModuleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotatingEquipments_MechanicalEquipmentId",
                table: "RotatingEquipments",
                column: "MechanicalEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_ApplicationUserId_PageKey",
                table: "SavedFilters",
                columns: new[] { "ApplicationUserId", "PageKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StaticEquipments_MechanicalEquipmentId",
                table: "StaticEquipments",
                column: "MechanicalEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_AssignedToEmployeeId",
                table: "TaskItems",
                column: "AssignedToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_RelatedType_RelatedId",
                table: "TaskItems",
                columns: new[] { "RelatedType", "RelatedId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_EmployeeId",
                table: "TeamMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_RoleId",
                table: "TeamMembers",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_EmployeeId_IsActive",
                table: "TeamMembers",
                columns: new[] { "TeamId", "EmployeeId", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Code",
                table: "Teams",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_DisciplineId",
                table: "Teams",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SupervisorEmployeeId",
                table: "Teams",
                column: "SupervisorEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalQueries_DisciplineId",
                table: "TechnicalQueries",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalQueries_ProjectId_QueryNumber",
                table: "TechnicalQueries",
                columns: new[] { "ProjectId", "QueryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalQueries_RelatedDocumentId",
                table: "TechnicalQueries",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalQueries_RelatedEquipmentId",
                table: "TechnicalQueries",
                column: "RelatedEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalItems_DocumentId",
                table: "TransmittalItems",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransmittalItems_TransmittalId",
                table: "TransmittalItems",
                column: "TransmittalId");

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_ProjectId",
                table: "Transmittals",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Transmittals_SentById",
                table: "Transmittals",
                column: "SentById");

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardWidgets_ApplicationUserId_WidgetKey",
                table: "UserDashboardWidgets",
                columns: new[] { "ApplicationUserId", "WidgetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VDRImportBatches_ProjectId",
                table: "VDRImportBatches",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRImportRows_VDRImportBatchId",
                table: "VDRImportRows",
                column: "VDRImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRItems_DocumentNumber",
                table: "VDRItems",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_VDRItems_EquipmentId",
                table: "VDRItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRItems_MRId",
                table: "VDRItems",
                column: "MRId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRItems_PoId",
                table: "VDRItems",
                column: "PoId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRItems_VendorId",
                table: "VDRItems",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRSubmissions_AttachmentId",
                table: "VDRSubmissions",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRSubmissions_ClientTransmittalId",
                table: "VDRSubmissions",
                column: "ClientTransmittalId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRSubmissions_CommentSheetAttachmentId",
                table: "VDRSubmissions",
                column: "CommentSheetAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRSubmissions_ReplySheetAttachmentId",
                table: "VDRSubmissions",
                column: "ReplySheetAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VDRSubmissions_VDRItemId_RevisionNo",
                table: "VDRSubmissions",
                columns: new[] { "VDRItemId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_Module_Category_Name",
                table: "WorkflowDefinitions",
                columns: new[] { "Module", "Category", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_WorkflowInstanceId",
                table: "WorkflowHistories",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_CurrentStageId",
                table: "WorkflowInstances",
                column: "CurrentStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Module_BusinessObjectType_BusinessObjectId_Status",
                table: "WorkflowInstances",
                columns: new[] { "Module", "BusinessObjectType", "BusinessObjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_WorkflowVersionId",
                table: "WorkflowInstances",
                column: "WorkflowVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceStages_AssignedToEmployeeId_Status",
                table: "WorkflowInstanceStages",
                columns: new[] { "AssignedToEmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceStages_WorkflowInstanceId",
                table: "WorkflowInstanceStages",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceStages_WorkflowStageId",
                table: "WorkflowInstanceStages",
                column: "WorkflowStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_WorkflowVersionId_StageOrder",
                table: "WorkflowStages",
                columns: new[] { "WorkflowVersionId", "StageOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitions_FromStageId",
                table: "WorkflowTransitions",
                column: "FromStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitions_ToStageId",
                table: "WorkflowTransitions",
                column: "ToStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitions_WorkflowVersionId",
                table: "WorkflowTransitions",
                column: "WorkflowVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersions_WorkflowDefinitionId_VersionNumber",
                table: "WorkflowVersions",
                columns: new[] { "WorkflowDefinitionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderDisciplineFindings_DisciplineId",
                table: "WorkOrderDisciplineFindings",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderDisciplineFindings_WorkOrderId",
                table: "WorkOrderDisciplineFindings",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderDisciplineReferrals_DisciplineId",
                table: "WorkOrderDisciplineReferrals",
                column: "DisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderDisciplineReferrals_WorkOrderId_DisciplineId_CycleNumber",
                table: "WorkOrderDisciplineReferrals",
                columns: new[] { "WorkOrderId", "DisciplineId", "CycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderLabors_EmployeeId",
                table: "WorkOrderLabors",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderLabors_TeamId",
                table: "WorkOrderLabors",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderLabors_WorkOrderId",
                table: "WorkOrderLabors",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedEmployeeId",
                table: "WorkOrders",
                column: "AssignedEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_AssignedTeamId",
                table: "WorkOrders",
                column: "AssignedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_EquipmentId",
                table: "WorkOrders",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_MaintenancePlanId",
                table: "WorkOrders",
                column: "MaintenancePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkOrderNumber",
                table: "WorkOrders",
                column: "WorkOrderNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AIDocumentClassifications_DocumentAttachments_AttachmentId",
                table: "AIDocumentClassifications",
                column: "AttachmentId",
                principalTable: "DocumentAttachments",
                principalColumn: "AttachmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AIExtractedSpecifications_DocumentAttachments_AttachmentId",
                table: "AIExtractedSpecifications",
                column: "AttachmentId",
                principalTable: "DocumentAttachments",
                principalColumn: "AttachmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AIExtractedTags_DocumentAttachments_AttachmentId",
                table: "AIExtractedTags",
                column: "AttachmentId",
                principalTable: "DocumentAttachments",
                principalColumn: "AttachmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AIExtractedTags_EquipmentTags_CreatedTagId",
                table: "AIExtractedTags",
                column: "CreatedTagId",
                principalTable: "EquipmentTags",
                principalColumn: "TagId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AIExtractedTags_Equipments_LinkedEquipmentId",
                table: "AIExtractedTags",
                column: "LinkedEquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Alarms_Instruments_InstrumentId",
                table: "Alarms",
                column: "InstrumentId",
                principalTable: "Instruments",
                principalColumn: "InstrumentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Equipments_EquipmentId",
                table: "Assets",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ControlLoops_Instruments_InstrumentId",
                table: "ControlLoops",
                column: "InstrumentId",
                principalTable: "Instruments",
                principalColumn: "InstrumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAttachments_EngineeringDocuments_DocumentId",
                table: "DocumentAttachments",
                column: "DocumentId",
                principalTable: "EngineeringDocuments",
                principalColumn: "DocumentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAttachments_Letters_LetterId",
                table: "DocumentAttachments",
                column: "LetterId",
                principalTable: "Letters",
                principalColumn: "LetterId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAttachments_MOCs_MOCId",
                table: "DocumentAttachments",
                column: "MOCId",
                principalTable: "MOCs",
                principalColumn: "MOCId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentAttachments_TechnicalQueries_TechnicalQueryId",
                table: "DocumentAttachments",
                column: "TechnicalQueryId",
                principalTable: "TechnicalQueries",
                principalColumn: "TechnicalQueryId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentComments_VDRSubmissions_VDRSubmissionId",
                table: "DocumentComments",
                column: "VDRSubmissionId",
                principalTable: "VDRSubmissions",
                principalColumn: "VDRSubmissionId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRevisions_EngineeringDocuments_DocumentId",
                table: "DocumentRevisions",
                column: "DocumentId",
                principalTable: "EngineeringDocuments",
                principalColumn: "DocumentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentTagLinks_EquipmentTags_EquipmentTagId",
                table: "DocumentTagLinks",
                column: "EquipmentTagId",
                principalTable: "EquipmentTags",
                principalColumn: "TagId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentWorkflows_EngineeringDocuments_DocumentId",
                table: "DocumentWorkflows",
                column: "DocumentId",
                principalTable: "EngineeringDocuments",
                principalColumn: "DocumentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ElectricalEquipments_Equipments_EquipmentId",
                table: "ElectricalEquipments",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_EngineeringDocuments_Equipments_EquipmentId",
                table: "EngineeringDocuments",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EngineeringDocuments_MaterialRequisitions_RelatedMRId",
                table: "EngineeringDocuments",
                column: "RelatedMRId",
                principalTable: "MaterialRequisitions",
                principalColumn: "MRId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Documents_DocumentId",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Employees_ApprovedById",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Employees_CheckedById",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Employees_DeletedById",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Employees_PreparedById",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitions_Employees_ResponsibleEngineerId",
                table: "MaterialRequisitions");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Equipments_EquipmentId",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Contracts_ContractId",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitions_Projects_ProjectId",
                table: "MaterialRequisitions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcurementPackages_Projects_ProjectId",
                table: "ProcurementPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringDocuments_Vendors_VendorId",
                table: "EngineeringDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitions_Disciplines_DisciplineId",
                table: "MaterialRequisitions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcurementPackages_Disciplines_DisciplineId",
                table: "ProcurementPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialRequisitions_EngineeringDocuments_RelatedDocumentId",
                table: "MaterialRequisitions");

            migrationBuilder.DropTable(
                name: "AIConfidenceSettings");

            migrationBuilder.DropTable(
                name: "AIDocumentClassifications");

            migrationBuilder.DropTable(
                name: "AIExtractedSpecifications");

            migrationBuilder.DropTable(
                name: "AIExtractedTags");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CableConnections");

            migrationBuilder.DropTable(
                name: "CauseEffects");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ContractDocuments");

            migrationBuilder.DropTable(
                name: "ControlLoops");

            migrationBuilder.DropTable(
                name: "DisciplineTasks");

            migrationBuilder.DropTable(
                name: "DocumentComments");

            migrationBuilder.DropTable(
                name: "DocumentIdcReferrals");

            migrationBuilder.DropTable(
                name: "DocumentRevisions");

            migrationBuilder.DropTable(
                name: "DocumentTagLinks");

            migrationBuilder.DropTable(
                name: "DocumentWorkflows");

            migrationBuilder.DropTable(
                name: "ElectricalEquipments");

            migrationBuilder.DropTable(
                name: "EmployeeLaborRates");

            migrationBuilder.DropTable(
                name: "EngineeringRelationships");

            migrationBuilder.DropTable(
                name: "EquipmentCriticalities");

            migrationBuilder.DropTable(
                name: "EquipmentDatasheets");

            migrationBuilder.DropTable(
                name: "EquipmentImportRows");

            migrationBuilder.DropTable(
                name: "EquipmentNozzles");

            migrationBuilder.DropTable(
                name: "FailureRecords");

            migrationBuilder.DropTable(
                name: "FunctionalLocations");

            migrationBuilder.DropTable(
                name: "InspectionChecklists");

            migrationBuilder.DropTable(
                name: "IOPoints");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseEntries");

            migrationBuilder.DropTable(
                name: "LetterRecipients");

            migrationBuilder.DropTable(
                name: "LetterReferrals");

            migrationBuilder.DropTable(
                name: "LetterStars");

            migrationBuilder.DropTable(
                name: "LetterTags");

            migrationBuilder.DropTable(
                name: "MaintenanceDocuments");

            migrationBuilder.DropTable(
                name: "MaintenanceHistories");

            migrationBuilder.DropTable(
                name: "MaintenanceSpareUsages");

            migrationBuilder.DropTable(
                name: "MaterialRequisitionItems");

            migrationBuilder.DropTable(
                name: "MaterialRequisitionQuotations");

            migrationBuilder.DropTable(
                name: "MDRImportRows");

            migrationBuilder.DropTable(
                name: "MeetingAttendees");

            migrationBuilder.DropTable(
                name: "NonConformanceReports");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropTable(
                name: "PipeSpecifications");

            migrationBuilder.DropTable(
                name: "PipingComponents");

            migrationBuilder.DropTable(
                name: "PipingLines");

            migrationBuilder.DropTable(
                name: "ProcurementApprovals");

            migrationBuilder.DropTable(
                name: "ProcurementCommunications");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "RotatingEquipments");

            migrationBuilder.DropTable(
                name: "RoutingRules");

            migrationBuilder.DropTable(
                name: "SavedFilters");

            migrationBuilder.DropTable(
                name: "StaticEquipments");

            migrationBuilder.DropTable(
                name: "TaskItems");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "TransmittalItems");

            migrationBuilder.DropTable(
                name: "UserDashboardWidgets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Valves");

            migrationBuilder.DropTable(
                name: "VDRImportRows");

            migrationBuilder.DropTable(
                name: "WorkflowHistories");

            migrationBuilder.DropTable(
                name: "WorkflowInstanceStages");

            migrationBuilder.DropTable(
                name: "WorkflowTransitions");

            migrationBuilder.DropTable(
                name: "WorkOrderDisciplineFindings");

            migrationBuilder.DropTable(
                name: "WorkOrderDisciplineReferrals");

            migrationBuilder.DropTable(
                name: "WorkOrderLabors");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Cables");

            migrationBuilder.DropTable(
                name: "Alarms");

            migrationBuilder.DropTable(
                name: "VDRSubmissions");

            migrationBuilder.DropTable(
                name: "EquipmentTags");

            migrationBuilder.DropTable(
                name: "EquipmentImportBatches");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "SpareParts");

            migrationBuilder.DropTable(
                name: "MDRImportBatches");

            migrationBuilder.DropTable(
                name: "Meetings");

            migrationBuilder.DropTable(
                name: "InspectionRecords");

            migrationBuilder.DropTable(
                name: "MechanicalEquipments");

            migrationBuilder.DropTable(
                name: "MaintenanceRoles");

            migrationBuilder.DropTable(
                name: "VDRImportBatches");

            migrationBuilder.DropTable(
                name: "WorkflowInstances");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "DocumentAttachments");

            migrationBuilder.DropTable(
                name: "Transmittals");

            migrationBuilder.DropTable(
                name: "VDRItems");

            migrationBuilder.DropTable(
                name: "ITPLineItems");

            migrationBuilder.DropTable(
                name: "WorkflowStages");

            migrationBuilder.DropTable(
                name: "MaintenancePlans");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Letters");

            migrationBuilder.DropTable(
                name: "MOCs");

            migrationBuilder.DropTable(
                name: "TechnicalQueries");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "InspectionTestPlans");

            migrationBuilder.DropTable(
                name: "WorkflowVersions");

            migrationBuilder.DropTable(
                name: "MaintenanceStrategies");

            migrationBuilder.DropTable(
                name: "QCDisciplines");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Equipments");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "Disciplines");

            migrationBuilder.DropTable(
                name: "EngineeringDocuments");

            migrationBuilder.DropTable(
                name: "MaterialRequisitions");

            migrationBuilder.DropTable(
                name: "ProcurementPackages");
        }
    }
}
