using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskManagerCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TaskItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemKey",
                table: "TaskItems",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedDate",
                table: "TaskItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentTaskItemId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Rank",
                table: "TaskItems",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ReporterEmployeeId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SprintId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "TaskItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StoryPoints",
                table: "TaskItems",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkItemStatusId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkItemTypeId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkSpaceId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkItemComments",
                columns: table => new
                {
                    WorkItemCommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskItemId = table.Column<int>(type: "int", nullable: false),
                    AuthorEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AuthorUserId = table.Column<int>(type: "int", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemComments", x => x.WorkItemCommentId);
                    table.ForeignKey(
                        name: "FK_WorkItemComments_TaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "TaskItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemLinks",
                columns: table => new
                {
                    WorkItemLinkId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceTaskItemId = table.Column<int>(type: "int", nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetTaskItemId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemLinks", x => x.WorkItemLinkId);
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_TaskItems_SourceTaskItemId",
                        column: x => x.SourceTaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "TaskItemId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemLinks_TaskItems_TargetTaskItemId",
                        column: x => x.TargetTaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "TaskItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemWatchers",
                columns: table => new
                {
                    WorkItemWatcherId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskItemId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemWatchers", x => x.WorkItemWatcherId);
                    table.ForeignKey(
                        name: "FK_WorkItemWatchers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemWatchers_TaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "TaskItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSpaces",
                columns: table => new
                {
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerDepartmentId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSpaces", x => x.WorkSpaceId);
                    table.ForeignKey(
                        name: "FK_WorkSpaces_Departments_OwnerDepartmentId",
                        column: x => x.OwnerDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sprints",
                columns: table => new
                {
                    SprintId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Goal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprints", x => x.SprintId);
                    table.ForeignKey(
                        name: "FK_Sprints_WorkSpaces_WorkSpaceId",
                        column: x => x.WorkSpaceId,
                        principalTable: "WorkSpaces",
                        principalColumn: "WorkSpaceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemStatuses",
                columns: table => new
                {
                    WorkItemStatusId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemStatuses", x => x.WorkItemStatusId);
                    table.ForeignKey(
                        name: "FK_WorkItemStatuses_WorkSpaces_WorkSpaceId",
                        column: x => x.WorkSpaceId,
                        principalTable: "WorkSpaces",
                        principalColumn: "WorkSpaceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemTypes",
                columns: table => new
                {
                    WorkItemTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameFa = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsSubtask = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemTypes", x => x.WorkItemTypeId);
                    table.ForeignKey(
                        name: "FK_WorkItemTypes_WorkSpaces_WorkSpaceId",
                        column: x => x.WorkSpaceId,
                        principalTable: "WorkSpaces",
                        principalColumn: "WorkSpaceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkSpaceMembers",
                columns: table => new
                {
                    WorkSpaceMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSpaceMembers", x => x.WorkSpaceMemberId);
                    table.ForeignKey(
                        name: "FK_WorkSpaceMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkSpaceMembers_WorkSpaces_WorkSpaceId",
                        column: x => x.WorkSpaceId,
                        principalTable: "WorkSpaces",
                        principalColumn: "WorkSpaceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemStatusTransitions",
                columns: table => new
                {
                    WorkItemStatusTransitionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkSpaceId = table.Column<int>(type: "int", nullable: false),
                    FromStatusId = table.Column<int>(type: "int", nullable: false),
                    ToStatusId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemStatusTransitions", x => x.WorkItemStatusTransitionId);
                    table.ForeignKey(
                        name: "FK_WorkItemStatusTransitions_WorkItemStatuses_FromStatusId",
                        column: x => x.FromStatusId,
                        principalTable: "WorkItemStatuses",
                        principalColumn: "WorkItemStatusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemStatusTransitions_WorkItemStatuses_ToStatusId",
                        column: x => x.ToStatusId,
                        principalTable: "WorkItemStatuses",
                        principalColumn: "WorkItemStatusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemStatusTransitions_WorkSpaces_WorkSpaceId",
                        column: x => x.WorkSpaceId,
                        principalTable: "WorkSpaces",
                        principalColumn: "WorkSpaceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ItemKey",
                table: "TaskItems",
                column: "ItemKey",
                unique: true,
                filter: "[ItemKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ParentTaskItemId",
                table: "TaskItems",
                column: "ParentTaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ReporterEmployeeId",
                table: "TaskItems",
                column: "ReporterEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_SprintId",
                table: "TaskItems",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_WorkItemStatusId",
                table: "TaskItems",
                column: "WorkItemStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_WorkItemTypeId",
                table: "TaskItems",
                column: "WorkItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_WorkSpaceId_IsActive_Rank",
                table: "TaskItems",
                columns: new[] { "WorkSpaceId", "IsActive", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_WorkSpaceId_State",
                table: "Sprints",
                columns: new[] { "WorkSpaceId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_Sprints_OneActivePerSpace",
                table: "Sprints",
                column: "WorkSpaceId",
                unique: true,
                filter: "[State] = N'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemComments_TaskItemId_CreatedDate",
                table: "WorkItemComments",
                columns: new[] { "TaskItemId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_SourceTaskItemId_LinkType_TargetTaskItemId",
                table: "WorkItemLinks",
                columns: new[] { "SourceTaskItemId", "LinkType", "TargetTaskItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLinks_TargetTaskItemId",
                table: "WorkItemLinks",
                column: "TargetTaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemStatuses_WorkSpaceId_Name",
                table: "WorkItemStatuses",
                columns: new[] { "WorkSpaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemStatusTransitions_FromStatusId",
                table: "WorkItemStatusTransitions",
                column: "FromStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemStatusTransitions_ToStatusId",
                table: "WorkItemStatusTransitions",
                column: "ToStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemStatusTransitions_WorkSpaceId_FromStatusId_ToStatusId",
                table: "WorkItemStatusTransitions",
                columns: new[] { "WorkSpaceId", "FromStatusId", "ToStatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTypes_WorkSpaceId_Name",
                table: "WorkItemTypes",
                columns: new[] { "WorkSpaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemWatchers_EmployeeId",
                table: "WorkItemWatchers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemWatchers_TaskItemId_EmployeeId",
                table: "WorkItemWatchers",
                columns: new[] { "TaskItemId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaceMembers_EmployeeId",
                table: "WorkSpaceMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaceMembers_WorkSpaceId_EmployeeId",
                table: "WorkSpaceMembers",
                columns: new[] { "WorkSpaceId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaces_Key",
                table: "WorkSpaces",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkSpaces_OwnerDepartmentId",
                table: "WorkSpaces",
                column: "OwnerDepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Employees_ReporterEmployeeId",
                table: "TaskItems",
                column: "ReporterEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Sprints_SprintId",
                table: "TaskItems",
                column: "SprintId",
                principalTable: "Sprints",
                principalColumn: "SprintId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskItemId",
                table: "TaskItems",
                column: "ParentTaskItemId",
                principalTable: "TaskItems",
                principalColumn: "TaskItemId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_WorkItemStatuses_WorkItemStatusId",
                table: "TaskItems",
                column: "WorkItemStatusId",
                principalTable: "WorkItemStatuses",
                principalColumn: "WorkItemStatusId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_WorkItemTypes_WorkItemTypeId",
                table: "TaskItems",
                column: "WorkItemTypeId",
                principalTable: "WorkItemTypes",
                principalColumn: "WorkItemTypeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_WorkSpaces_WorkSpaceId",
                table: "TaskItems",
                column: "WorkSpaceId",
                principalTable: "WorkSpaces",
                principalColumn: "WorkSpaceId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Employees_ReporterEmployeeId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Sprints_SprintId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskItemId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_WorkItemStatuses_WorkItemStatusId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_WorkItemTypes_WorkItemTypeId",
                table: "TaskItems");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_WorkSpaces_WorkSpaceId",
                table: "TaskItems");

            migrationBuilder.DropTable(
                name: "Sprints");

            migrationBuilder.DropTable(
                name: "WorkItemComments");

            migrationBuilder.DropTable(
                name: "WorkItemLinks");

            migrationBuilder.DropTable(
                name: "WorkItemStatusTransitions");

            migrationBuilder.DropTable(
                name: "WorkItemTypes");

            migrationBuilder.DropTable(
                name: "WorkItemWatchers");

            migrationBuilder.DropTable(
                name: "WorkSpaceMembers");

            migrationBuilder.DropTable(
                name: "WorkItemStatuses");

            migrationBuilder.DropTable(
                name: "WorkSpaces");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ItemKey",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ParentTaskItemId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ReporterEmployeeId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_SprintId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_WorkItemStatusId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_WorkItemTypeId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_WorkSpaceId_IsActive_Rank",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ItemKey",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ModifiedDate",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ParentTaskItemId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ReporterEmployeeId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "StoryPoints",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "WorkItemStatusId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "WorkItemTypeId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "WorkSpaceId",
                table: "TaskItems");
        }
    }
}
