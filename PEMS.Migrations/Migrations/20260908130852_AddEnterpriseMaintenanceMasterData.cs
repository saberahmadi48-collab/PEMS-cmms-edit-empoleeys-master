using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseMaintenanceMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemDefault",
                table: "MaintenanceGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentClassId",
                table: "Equipments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentClasses",
                columns: table => new
                {
                    EquipmentClassId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentClasses", x => x.EquipmentClassId);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceStrategyTypes",
                columns: table => new
                {
                    MaintenanceStrategyTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStrategyTypes", x => x.MaintenanceStrategyTypeId);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTypes",
                columns: table => new
                {
                    MaintenanceTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTypes", x => x.MaintenanceTypeId);
                });

            migrationBuilder.CreateTable(
                name: "StandardReferences",
                columns: table => new
                {
                    StandardReferenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StandardNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TitleFa = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardReferences", x => x.StandardReferenceId);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivities",
                columns: table => new
                {
                    MaintenanceActivityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaintenanceGroupId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceTypeId = table.Column<int>(type: "int", nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivities", x => x.MaintenanceActivityId);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivities_MaintenanceGroups_MaintenanceGroupId",
                        column: x => x.MaintenanceGroupId,
                        principalTable: "MaintenanceGroups",
                        principalColumn: "MaintenanceGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivities_MaintenanceTypes_MaintenanceTypeId",
                        column: x => x.MaintenanceTypeId,
                        principalTable: "MaintenanceTypes",
                        principalColumn: "MaintenanceTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivityEquipmentClasses",
                columns: table => new
                {
                    MaintenanceActivityId = table.Column<int>(type: "int", nullable: false),
                    EquipmentClassId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivityEquipmentClasses", x => new { x.MaintenanceActivityId, x.EquipmentClassId });
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityEquipmentClasses_EquipmentClasses_EquipmentClassId",
                        column: x => x.EquipmentClassId,
                        principalTable: "EquipmentClasses",
                        principalColumn: "EquipmentClassId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityEquipmentClasses_MaintenanceActivities_MaintenanceActivityId",
                        column: x => x.MaintenanceActivityId,
                        principalTable: "MaintenanceActivities",
                        principalColumn: "MaintenanceActivityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivityStandardReferences",
                columns: table => new
                {
                    MaintenanceActivityId = table.Column<int>(type: "int", nullable: false),
                    StandardReferenceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivityStandardReferences", x => new { x.MaintenanceActivityId, x.StandardReferenceId });
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityStandardReferences_MaintenanceActivities_MaintenanceActivityId",
                        column: x => x.MaintenanceActivityId,
                        principalTable: "MaintenanceActivities",
                        principalColumn: "MaintenanceActivityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityStandardReferences_StandardReferences_StandardReferenceId",
                        column: x => x.StandardReferenceId,
                        principalTable: "StandardReferences",
                        principalColumn: "StandardReferenceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivityStrategyTypes",
                columns: table => new
                {
                    MaintenanceActivityId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceStrategyTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivityStrategyTypes", x => new { x.MaintenanceActivityId, x.MaintenanceStrategyTypeId });
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityStrategyTypes_MaintenanceActivities_MaintenanceActivityId",
                        column: x => x.MaintenanceActivityId,
                        principalTable: "MaintenanceActivities",
                        principalColumn: "MaintenanceActivityId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivityStrategyTypes_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                        column: x => x.MaintenanceStrategyTypeId,
                        principalTable: "MaintenanceStrategyTypes",
                        principalColumn: "MaintenanceStrategyTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies",
                column: "MaintenanceStrategyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_EquipmentClassId",
                table: "Equipments",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentClasses_Code",
                table: "EquipmentClasses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_Code",
                table: "MaintenanceActivities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_MaintenanceGroupId",
                table: "MaintenanceActivities",
                column: "MaintenanceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_MaintenanceTypeId",
                table: "MaintenanceActivities",
                column: "MaintenanceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivityEquipmentClasses_EquipmentClassId",
                table: "MaintenanceActivityEquipmentClasses",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivityStandardReferences_StandardReferenceId",
                table: "MaintenanceActivityStandardReferences",
                column: "StandardReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivityStrategyTypes_MaintenanceStrategyTypeId",
                table: "MaintenanceActivityStrategyTypes",
                column: "MaintenanceStrategyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategyTypes_Code",
                table: "MaintenanceStrategyTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTypes_Code",
                table: "MaintenanceTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandardReferences_Code",
                table: "StandardReferences",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandardReferences_Organization_StandardNumber",
                table: "StandardReferences",
                columns: new[] { "Organization", "StandardNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Equipments_EquipmentClasses_EquipmentClassId",
                table: "Equipments",
                column: "EquipmentClassId",
                principalTable: "EquipmentClasses",
                principalColumn: "EquipmentClassId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies",
                column: "MaintenanceStrategyTypeId",
                principalTable: "MaintenanceStrategyTypes",
                principalColumn: "MaintenanceStrategyTypeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Equipments_EquipmentClasses_EquipmentClassId",
                table: "Equipments");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_MaintenanceStrategyTypes_MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropTable(
                name: "MaintenanceActivityEquipmentClasses");

            migrationBuilder.DropTable(
                name: "MaintenanceActivityStandardReferences");

            migrationBuilder.DropTable(
                name: "MaintenanceActivityStrategyTypes");

            migrationBuilder.DropTable(
                name: "EquipmentClasses");

            migrationBuilder.DropTable(
                name: "StandardReferences");

            migrationBuilder.DropTable(
                name: "MaintenanceActivities");

            migrationBuilder.DropTable(
                name: "MaintenanceStrategyTypes");

            migrationBuilder.DropTable(
                name: "MaintenanceTypes");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_EquipmentClassId",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "MaintenanceStrategyTypeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "IsSystemDefault",
                table: "MaintenanceGroups");

            migrationBuilder.DropColumn(
                name: "EquipmentClassId",
                table: "Equipments");
        }
    }
}
