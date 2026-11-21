# Data-driven sidebar (dynamic sidebar)

Status: implemented behind a feature flag, **off by default**. The migration exists but is **not applied** to `PEMSDB` or any shared database.

## Owner decisions (2026-10-15)

| Topic | Decision |
|---|---|
| Scope | Data-driven menu managed by Admin; per-item visibility by permission; live badges |
| Schema | Additive migration approved (new table only) |
| Who edits the menu | SystemAdmin only |
| Items whose page has no permission check | Stay visible, as today; labelled "No permission signal" in Admin |
| Badges in v1 | My Work, unread notifications, my open action items, module counts |

## How it works

- **Table `NavigationMenuItems`** (migration `20261014213320_AddNavigationMenuItems`): sections, subheadings and links with a stable `Key`, parent, order, title key, optional Persian/English title override, icon, route, visibility rule and badge key.
- **Catalog in code** (`PEMS.Web/Navigation/NavigationMenuDefaults.cs`): an exact mirror of the legacy `_SidebarMenu.cshtml`, with the same links, icons, title keys and order. A test reads the legacy file and compares them one by one.
  - At startup (flag on), the catalog is synced into the table. The sync only adds missing items and updates code-owned fields: route, visibility rule, kind and active-state hints.
  - It never overwrites admin-owned fields: order, parent, icon, titles, hidden/visible and badge.
- **Visibility rules**: `|` means OR and `&` means AND. Atoms:
  - `Always`
  - `Module:<key>`: the existing `NavigationVisibilityResolver`, including admin overrides
  - `CmmsItem:<key>`
  - `Perm:<Resource>.<Action>`
  - `RoleAction:<Module>.<Action>`
  - `Flag:<name>`: QuickSetup, ImportCenter, PartCatalogue, PartCatalogueAdmin, Replenishment, DisciplineMaster
  - `Role:<name>`

  An unknown atom hides the item (fails closed). Each link's `Perm:` is the permission its target action actually enforces; it was read from the source, not invented. Hiding a link never changes what a user may do; the target controller still decides.
- **Empty sections and subheadings** are not rendered.
- **Active state**: the link with the exact action wins. Otherwise the only link for that controller is used, otherwise the one marked `IsDefaultForController` (for example Letter/Index versus MyReferrals). Sections also open for their extra controllers or area.
- **Badges** (`NavigationBadges.cs`): a closed list. Module counts (`workorders.open` needs `WorkOrder.View`; `storerequisitions.awaitingapproval` needs `StoreRequisition.Approve`) are computed only for holders of the permission. Personal counts come from the user's own context.
  - The endpoint is `GET /NavigationBadge/Counts`. Identity comes from the cookie only.
  - `sidebar-badges.js` refreshes on load, every 60 s, when the tab becomes visible, and on every SignalR notification event (`pems:notifications-changed`, dispatched by `notifications-hub.js`).
- **Admin**: *Administration → User & Access → Navigation Menu* (`Admin/NavigationMenu`), SystemAdmin only, 404 while the flag is off. The admin can:
  - reorder items (up/down buttons, keyboard accessible);
  - move an item to another valid parent;
  - set Persian and English titles;
  - set the icon (validated `bi-*`) and the badge (from the list);
  - hide or show an item.

  Custom links may point only to an existing GET action of this application, with an optional existing `<Resource>.View` permission. Only custom links can be deleted. Edits use `RowVersion` (a concurrent edit is refused).

## Deploying (needs separate approval)

1. Back up the target database.
2. Apply the migration (`dotnet ef database update` or the deployment kit's idempotent script). It creates one table and two indexes; `Down` drops the table.
3. Set `Features:DynamicSidebar:Enabled=true` and restart. The catalog is seeded at startup.
4. To roll back the UI, set the flag back to `false`; the legacy sidebar returns immediately. The table can stay.

## Behaviour change versus the legacy sidebar

With the flag on, a link is hidden when the user lacks the permission its page enforces (for example Viewer no longer sees Vendors/Contracts links that would return 403). Section-level visibility and admin overrides are unchanged. New link: *Navigation Menu* (SystemAdmin).

## Tests

- `DynamicSidebarTests` (44, InMemory): legacy parity, catalog integrity (routes exist, rules valid, keys in both languages), visibility per representative role, active state, overrides, empty subheading, reorder, title override, sync, validation, cache, badges, admin controller.
- `DynamicSidebarSqlTests` (disposable `PEMS_TEST_P03_*` database): migration, idempotent sync, unique `Key` index, `RowVersion` concurrency.
- Regression (2026-10-15): Authorization, Procurement, Inventory, Web, Security, Cmms and Organization groups, excluding SQL/concurrency/HTTP-host classes: 7,129 of 7,129 passed.
