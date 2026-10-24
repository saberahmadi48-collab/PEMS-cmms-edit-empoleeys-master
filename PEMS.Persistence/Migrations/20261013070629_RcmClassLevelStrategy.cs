using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RcmClassLevelStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_Equipments_EquipmentId",
                table: "MaintenanceStrategies");

            migrationBuilder.AlterColumn<int>(
                name: "EquipmentId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<decimal>(
                name: "AlarmThreshold",
                table: "MaintenanceStrategies",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "MaintenanceStrategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "MaintenanceStrategies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByEmployeeId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorEmployeeId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CbmParameter",
                table: "MaintenanceStrategies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultIntervalDays",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EquipmentClassId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDurationHours",
                table: "MaintenanceStrategies",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureCauseId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureEffect",
                table: "MaintenanceStrategies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureModeId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "MaintenanceStrategies",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "FunctionalFailure",
                table: "MaintenanceStrategies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionIntervalDays",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHiddenFailure",
                table: "MaintenanceStrategies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OwnerDisciplineId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PfIntervalDays",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredSkills",
                table: "MaintenanceStrategies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                table: "MaintenanceStrategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewDate",
                table: "MaintenanceStrategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "MaintenanceStrategies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MaintenanceStrategies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "MaintenanceStrategies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedByEmployeeId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersedesStrategyId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskType",
                table: "MaintenanceStrategies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThresholdUnit",
                table: "MaintenanceStrategies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "MaintenanceStrategies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TripThreshold",
                table: "MaintenanceStrategies",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCompletionDate",
                table: "MaintenancePlans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleMode",
                table: "MaintenancePlans",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Fixed");

            migrationBuilder.CreateTable(
                name: "MaintenanceStrategyAssignments",
                columns: table => new
                {
                    MaintenanceStrategyAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceStrategyId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    OverrideIntervalDays = table.Column<int>(type: "int", nullable: true),
                    OverrideTaskNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OverrideJustification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OverrideStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OverrideRequestedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    OverrideApprovedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    OverrideApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStrategyAssignments", x => x.MaintenanceStrategyAssignmentId);
                    table.ForeignKey(
                        name: "FK_MaintenanceStrategyAssignments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceStrategyAssignments_MaintenanceStrategies_MaintenanceStrategyId",
                        column: x => x.MaintenanceStrategyId,
                        principalTable: "MaintenanceStrategies",
                        principalColumn: "MaintenanceStrategyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceStrategyHistories",
                columns: table => new
                {
                    MaintenanceStrategyHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaintenanceStrategyId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceStrategyAssignmentId = table.Column<int>(type: "int", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ChangesSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStrategyHistories", x => x.MaintenanceStrategyHistoryId);
                    table.ForeignKey(
                        name: "FK_MaintenanceStrategyHistories_MaintenanceStrategies_MaintenanceStrategyId",
                        column: x => x.MaintenanceStrategyId,
                        principalTable: "MaintenanceStrategies",
                        principalColumn: "MaintenanceStrategyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_EquipmentClassId",
                table: "MaintenanceStrategies",
                column: "EquipmentClassId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_FailureCauseId",
                table: "MaintenanceStrategies",
                column: "FailureCauseId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_FailureModeId",
                table: "MaintenanceStrategies",
                column: "FailureModeId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_FamilyId",
                table: "MaintenanceStrategies",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_OwnerDisciplineId",
                table: "MaintenanceStrategies",
                column: "OwnerDisciplineId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategies_Status_IsActive",
                table: "MaintenanceStrategies",
                columns: new[] { "Status", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans",
                column: "MaintenanceStrategyAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategyAssignments_EquipmentId",
                table: "MaintenanceStrategyAssignments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "UX_MaintenanceStrategyAssignments_Strategy_Equipment_Active",
                table: "MaintenanceStrategyAssignments",
                columns: new[] { "MaintenanceStrategyId", "EquipmentId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStrategyHistories_Strategy_Timestamp",
                table: "MaintenanceStrategyHistories",
                columns: new[] { "MaintenanceStrategyId", "Timestamp" });

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenancePlans_MaintenanceStrategyAssignments_MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans",
                column: "MaintenanceStrategyAssignmentId",
                principalTable: "MaintenanceStrategyAssignments",
                principalColumn: "MaintenanceStrategyAssignmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_Disciplines_OwnerDisciplineId",
                table: "MaintenanceStrategies",
                column: "OwnerDisciplineId",
                principalTable: "Disciplines",
                principalColumn: "DisciplineId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_EquipmentClasses_EquipmentClassId",
                table: "MaintenanceStrategies",
                column: "EquipmentClassId",
                principalTable: "EquipmentClasses",
                principalColumn: "EquipmentClassId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_Equipments_EquipmentId",
                table: "MaintenanceStrategies",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_FailureCauses_FailureCauseId",
                table: "MaintenanceStrategies",
                column: "FailureCauseId",
                principalTable: "FailureCauses",
                principalColumn: "FailureCauseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_FailureModes_FailureModeId",
                table: "MaintenanceStrategies",
                column: "FailureModeId",
                principalTable: "FailureModes",
                principalColumn: "FailureModeId",
                onDelete: ReferentialAction.Restrict);

            // ----------------------------------------------------------------------
            // مهاجرت داده (Idempotent، بدون حذف داده): برچسب‌های متنی → کد پایدار،
            // استراتژی تک‌تگی → الگو + Assignment، اتصال Plan ها. جزئیات: RcmDataMigrationSql.
            // ----------------------------------------------------------------------
            foreach (var sql in PEMS.Persistence.DataMigrations.RcmDataMigrationSql.UpStatements)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenancePlans_MaintenanceStrategyAssignments_MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_Disciplines_OwnerDisciplineId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_EquipmentClasses_EquipmentClassId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_Equipments_EquipmentId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_FailureCauses_FailureCauseId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceStrategies_FailureModes_FailureModeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropTable(
                name: "MaintenanceStrategyAssignments");

            migrationBuilder.DropTable(
                name: "MaintenanceStrategyHistories");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_EquipmentClassId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_FailureCauseId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_FailureModeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_FamilyId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_OwnerDisciplineId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceStrategies_Status_IsActive",
                table: "MaintenanceStrategies");

            migrationBuilder.DropIndex(
                name: "IX_MaintenancePlans_MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans");

            migrationBuilder.DropColumn(
                name: "AlarmThreshold",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ApprovedByEmployeeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "AuthorEmployeeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "CbmParameter",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "DefaultIntervalDays",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "EquipmentClassId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "EstimatedDurationHours",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "FailureCauseId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "FailureEffect",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "FailureModeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "FunctionalFailure",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "InspectionIntervalDays",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "IsHiddenFailure",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "OwnerDisciplineId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "PfIntervalDays",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "RequiredSkills",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ReviewDate",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "SubmittedByEmployeeId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "SupersedesStrategyId",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "ThresholdUnit",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "TripThreshold",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "MaintenanceStrategies");

            migrationBuilder.DropColumn(
                name: "LastCompletionDate",
                table: "MaintenancePlans");

            migrationBuilder.DropColumn(
                name: "MaintenanceStrategyAssignmentId",
                table: "MaintenancePlans");

            migrationBuilder.DropColumn(
                name: "ScheduleMode",
                table: "MaintenancePlans");

            migrationBuilder.AlterColumn<int>(
                name: "EquipmentId",
                table: "MaintenanceStrategies",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceStrategies_Equipments_EquipmentId",
                table: "MaintenanceStrategies",
                column: "EquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
