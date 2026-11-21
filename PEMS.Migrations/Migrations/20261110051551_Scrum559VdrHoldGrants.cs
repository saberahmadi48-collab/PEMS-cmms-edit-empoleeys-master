using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <summary>
    /// SCRUM-559 (owner decision 2026-11-10): VDR items can be put on hold and released, by the same roles as Engineering Documents:
    /// Hold for EngineeringManager, DocumentController and DisciplineEngineer, Release for the first two (EngineeringSupervisor gets both through
    /// the startup seeder, which adds a role row that is missing). Existing VDR rows are updated here, once. Data only, no schema change.
    /// Down leaves the grants alone, because it cannot tell a later admin change apart from this one.
    /// </summary>
    public partial class Scrum559VdrHoldGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [RolePermissions] SET [CanHold] = 1, [CanReleaseHold] = 1
WHERE [ModuleName] = N'VDR' AND [RoleName] IN (N'EngineeringManager', N'DocumentController', N'EngineeringSupervisor');

UPDATE [RolePermissions] SET [CanHold] = 1
WHERE [ModuleName] = N'VDR' AND [RoleName] = N'DisciplineEngineer';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the summary.
        }
    }
}
