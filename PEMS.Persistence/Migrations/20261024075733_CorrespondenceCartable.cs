using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrespondenceCartable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Tags",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentTagId",
                table: "Tags",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Tags",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LetterContactGroups",
                columns: table => new
                {
                    LetterContactGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterContactGroups", x => x.LetterContactGroupId);
                });

            migrationBuilder.CreateTable(
                name: "LetterDelegations",
                columns: table => new
                {
                    LetterDelegationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegatorUserId = table.Column<int>(type: "int", nullable: false),
                    DelegateUserId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RevokedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterDelegations", x => x.LetterDelegationId);
                });

            migrationBuilder.CreateTable(
                name: "LetterReminders",
                columns: table => new
                {
                    LetterReminderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    RemindAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    NotifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterReminders", x => x.LetterReminderId);
                    table.ForeignKey(
                        name: "FK_LetterReminders_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterRoutingRules",
                columns: table => new
                {
                    LetterRoutingRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MatchLetterType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MatchPriority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MatchCategory = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    MatchProjectId = table.Column<int>(type: "int", nullable: true),
                    MatchDisciplineId = table.Column<int>(type: "int", nullable: true),
                    MatchSenderContains = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    MatchSubjectContains = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ApplyTagId = table.Column<int>(type: "int", nullable: true),
                    StarLetter = table.Column<bool>(type: "bit", nullable: false),
                    ReferToEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReferInstruction = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MatchCount = table.Column<int>(type: "int", nullable: false),
                    LastMatchedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterRoutingRules", x => x.LetterRoutingRuleId);
                });

            migrationBuilder.CreateTable(
                name: "LetterUserSettings",
                columns: table => new
                {
                    LetterUserSettingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterUserSettings", x => x.LetterUserSettingId);
                });

            migrationBuilder.CreateTable(
                name: "LetterUserStates",
                columns: table => new
                {
                    LetterUserStateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false),
                    IsTrashed = table.Column<bool>(type: "bit", nullable: false),
                    TrashedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsPurged = table.Column<bool>(type: "bit", nullable: false),
                    PurgedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterUserStates", x => x.LetterUserStateId);
                    table.ForeignKey(
                        name: "FK_LetterUserStates_Letters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "Letters",
                        principalColumn: "LetterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LetterContactGroupMembers",
                columns: table => new
                {
                    LetterContactGroupMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterContactGroupId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    CorrespondentPartyId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LetterContactGroupMembers", x => x.LetterContactGroupMemberId);
                    table.ForeignKey(
                        name: "FK_LetterContactGroupMembers_CorrespondentParties_CorrespondentPartyId",
                        column: x => x.CorrespondentPartyId,
                        principalTable: "CorrespondentParties",
                        principalColumn: "CorrespondentPartyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterContactGroupMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LetterContactGroupMembers_LetterContactGroups_LetterContactGroupId",
                        column: x => x.LetterContactGroupId,
                        principalTable: "LetterContactGroups",
                        principalColumn: "LetterContactGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OwnerUserId_ParentTagId",
                table: "Tags",
                columns: new[] { "OwnerUserId", "ParentTagId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_ParentTagId",
                table: "Tags",
                column: "ParentTagId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterContactGroupMembers_CorrespondentPartyId",
                table: "LetterContactGroupMembers",
                column: "CorrespondentPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterContactGroupMembers_EmployeeId",
                table: "LetterContactGroupMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterContactGroupMembers_LetterContactGroupId_EmployeeId_CorrespondentPartyId",
                table: "LetterContactGroupMembers",
                columns: new[] { "LetterContactGroupId", "EmployeeId", "CorrespondentPartyId" },
                unique: true,
                filter: "[EmployeeId] IS NOT NULL AND [CorrespondentPartyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LetterContactGroups_OwnerUserId_IsActive",
                table: "LetterContactGroups",
                columns: new[] { "OwnerUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterDelegations_DelegateUserId_IsActive",
                table: "LetterDelegations",
                columns: new[] { "DelegateUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterDelegations_DelegatorUserId_IsActive",
                table: "LetterDelegations",
                columns: new[] { "DelegatorUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterReminders_LetterId",
                table: "LetterReminders",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_LetterReminders_OwnerUserId_IsDone_RemindAt",
                table: "LetterReminders",
                columns: new[] { "OwnerUserId", "IsDone", "RemindAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterRoutingRules_OwnerUserId_IsActive_SortOrder",
                table: "LetterRoutingRules",
                columns: new[] { "OwnerUserId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterUserSettings_ApplicationUserId",
                table: "LetterUserSettings",
                column: "ApplicationUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LetterUserStates_ApplicationUserId_IsTrashed",
                table: "LetterUserStates",
                columns: new[] { "ApplicationUserId", "IsTrashed" });

            migrationBuilder.CreateIndex(
                name: "IX_LetterUserStates_LetterId_ApplicationUserId",
                table: "LetterUserStates",
                columns: new[] { "LetterId", "ApplicationUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_Tags_ParentTagId",
                table: "Tags",
                column: "ParentTagId",
                principalTable: "Tags",
                principalColumn: "TagId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_Tags_ParentTagId",
                table: "Tags");

            migrationBuilder.DropTable(
                name: "LetterContactGroupMembers");

            migrationBuilder.DropTable(
                name: "LetterDelegations");

            migrationBuilder.DropTable(
                name: "LetterReminders");

            migrationBuilder.DropTable(
                name: "LetterRoutingRules");

            migrationBuilder.DropTable(
                name: "LetterUserSettings");

            migrationBuilder.DropTable(
                name: "LetterUserStates");

            migrationBuilder.DropTable(
                name: "LetterContactGroups");

            migrationBuilder.DropIndex(
                name: "IX_Tags_OwnerUserId_ParentTagId",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_ParentTagId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ParentTagId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Tags");
        }
    }
}
