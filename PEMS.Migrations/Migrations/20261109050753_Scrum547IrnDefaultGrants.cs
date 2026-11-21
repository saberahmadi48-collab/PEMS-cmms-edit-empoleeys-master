using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <summary>
    /// SCRUM-547 (owner decision 2026-11-09): receiving against PO lines could not be finished with the default roles. The Warehouse Manager
    /// gets IRN Edit (to resolve the Hold routed to them) and the Buyer gets IRN View (to open the IRN and decide close short / replacement).
    /// RoleCatalog writes default grants only when it creates a role, so existing databases get them here, once. Data only, no schema change.
    /// A role that does not exist is skipped. A Warehouse Manager IRN row the admin deleted is not re-created. Down leaves the grants alone,
    /// because it cannot tell a later admin change apart from this one.
    /// </summary>
    public partial class Scrum547IrnDefaultGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [RolePermissions] SET [CanView] = 1, [CanEdit] = 1
WHERE [RoleName] = N'WH.Manager' AND [ModuleName] = N'IRN';

UPDATE [RolePermissions] SET [CanView] = 1
WHERE [RoleName] = N'PRC.Buyer' AND [ModuleName] = N'IRN';

INSERT INTO [RolePermissions] ([RoleName], [ModuleName], [CanView], [CanCreate], [CanEdit], [CanDelete], [CanApprove],
    [CanManualNumber], [CanFreeze], [CanUnfreeze], [CanHold], [CanReleaseHold], [CreatedDate])
SELECT N'PRC.Buyer', N'IRN', 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, GETDATE()
WHERE EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'PRC.BUYER')
  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] WHERE [RoleName] = N'PRC.Buyer' AND [ModuleName] = N'IRN');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the summary.
        }
    }
}
