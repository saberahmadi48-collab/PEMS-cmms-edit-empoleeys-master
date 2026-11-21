# List page component (SCRUM-337)

Status: 2026-10-31, waves 0, 1, 2, 2b, 3, 4a and 4b. The look comes from the correspondence list and the Engineering Documents list (SCRUM-408).
It lives in `PEMS.UI` (ADR-0007: Tag Helper facade for MVC pages). Texts are inputs (SCRUM-416).

## What the component gives every list page

- The table fills the window. The header is sticky, the rows scroll inside, and the pager stays at the bottom. On
  narrow screens the page flows normally.
- One toolbar: the title with its count, then icon tools. Search opens over the title. Filters open in a dropdown
  with a count badge. A KPI toggle remembers its state per browser. There are buttons for Excel and for resetting
  the columns (hidden on phones), and a "New ..." action.
- KPI chips are hidden until the user asks for them, and active filters show as removable chips.
- An empty state with "clear all" when the list is filtered.
- The same CSS and JS for every page: `_content/PEMS.UI/pems-list.css` and `_content/PEMS.UI/pems-list.js`.

## What stays with the page (its own rules)

Each page keeps its own:
- columns, cells and row colouring;
- row actions;
- filters, and which ones count as active;
- search target (`search-type` for the shared autocomplete);
- permissions (show a tool only when the user may use it);
- export link, paging and query-string handling;
- KPI content;
- sub-navigation.

A tool that the page does not render does not appear.

## Skeleton

```cshtml
@using PEMS.Web.Helpers
@section Styles { <link rel="stylesheet" href="~/_content/PEMS.UI/pems-list.css" asp-append-version="true" /> }

<pems-list-page texts="@ListPageTexts.From(Localizer)">
    @* optional module sub-navigation (vs-subnav) *@
    <pems-list-panel>
        <pems-list-toolbar heading="@Localizer["..."]" heading-title="optional tooltip" total="@Model.TotalCount">
            <pems-list-search id="xSearch" action="@Url.Action("Index")" input-id="xQ" name="search" value="@search"
                              placeholder="@Localizer["..."]" search-type="OptionalAutocompleteType">
                @* hidden inputs that keep the active filters *@
            </pems-list-search>
            @* live-filter="true": for a short list without server paging, typing filters the rows on the page and nothing is submitted *@
            <pems-list-tools search-for="xSearch">
                <pems-list-filters form-id="xFilter" action="@Url.Action("Index")" count="@activeCount" reset-href="@Url.Action("Index", new { search })">
                    <input type="hidden" name="search" value="@search" />
                    <div><label class="lp-lbl" for="...">...</label><select ... class="form-select form-select-sm">...</select></div>
                </pems-list-filters>
                <pems-list-kpi-toggle target="xKpis" storage-key="pems.<page>.kpis" />
                <pems-list-export href="@Url.Action("Export", new { ... })" />
                <pems-list-reset-columns grid-key="X" />
                <pems-list-new href="@Url.Action("Create")" text="@Localizer["..."]" />
            </pems-list-tools>
        </pems-list-toolbar>

        <pems-list-kpis id="xKpis"> <span class="vs-chip">... <strong>..</strong></span> </pems-list-kpis>

        <pems-list-chips reset-href="@Url.Action("Index", new { search })">
            @if (...) { <pems-list-chip label="..." value="..." remove-href="@Url.Action("Index", new { /* the others */ })" /> }
        </pems-list-chips>

        <pems-list-grid>
            <pems-list-table grid-key="X" empty="@(!Model.Items.Any())" empty-text="@Localizer["..."]"
                             filtered="@(activeCount > 0 || !string.IsNullOrEmpty(search))" clear-href="@Url.Action("Index")">
                <thead>...</thead>
                <tbody> @foreach (...) { <tr class="lp-row">...</tr> } </tbody>
            </pems-list-table>
            <pems-list-footer> @await Html.PartialAsync("_Pagination", ...) </pems-list-footer>
        </pems-list-grid>
    </pems-list-panel>
</pems-list-page>

@section Scripts { <script src="~/_content/PEMS.UI/pems-list.js" asp-append-version="true"></script> }
```

Optional tools added in wave 1:

```cshtml
@* a tool with its own dropdown (quick filters, saved views, ...): li items only *@
<pems-list-menu id="xViewsBtn" icon="bi-bookmark" label="@Localizer["..."]">
    <li><a class="dropdown-item" href="...">...</a></li>
</pems-list-menu>

@* bulk bar, between the chips and the grid: shown while rows are ticked; every form inside gets the ticked ids *@
<pems-list-bulk count-text="@Localizer["Lp_SelectedCount"]" name="ids">
    <form asp-action="ExportSelected" method="post"><button type="submit" class="btn btn-sm btn-outline-success">...</button></form>
</pems-list-bulk>
@* header cell: <input type="checkbox" class="form-check-input" data-lp-select-all aria-label="@Localizer["Lp_SelectAll"]" />
   row cell:    <input type="checkbox" class="form-check-input" value="@id" data-lp-select aria-label="..." />  (th/td class lp-select) *@
```

Options added in wave 2:

```cshtml
@* "New ..." that opens the page's own Bootstrap modal instead of a page *@
<pems-list-new modal="createPartModal" text="@Localizer["..."]" />

@* an export that is not the filtered list (for example every row) says so in its own label *@
<pems-list-export href="@Url.Action("Export")" label="@Localizer["..."]" />

@* a TempData message sits above the panel, as a direct child of the page *@
@if (TempData["Message"] != null) { <div class="alert alert-success py-2 px-3 small">@TempData["Message"]</div> }
```

Option added in wave 2b: `<pems-list-kpis id="x" open="true">` with `<pems-list-kpi-toggle ... open="true" />` shows the KPI row first,
for pages whose counts are the point (RCM / FMEA coverage); a choice the viewer stored still wins.

Also added in wave 2b, so a page writes no Bootstrap control by hand (the SCRUM-341 adoption ratchet counts them):

- `<pems-list-filter-select id="st" name="status" label="..." value="@current" all-text="..." items="@list.Select(x => new SelectListItem(text, value))" />`
  inside `<pems-list-filters>`: label, an "all" option when `all-text` is set, the current value selected.
- `<pems-list-popover id="up" icon="bi-cloud-upload" label="..." primary="true">...</pems-list-popover>` in `<pems-list-tools>`:
  a tool (with `primary`, a labelled button like New) that opens a dropdown panel with the page's short form, for example an upload.
  Use it instead of a new modal; a page's existing modal stays until SCRUM-482 moves it to `PemsModal`.

A page note (what the list is, where to add a record) becomes the `heading-title` tool tip of the title, not a
paragraph above the table. Page actions that are not "New" (for example "Generate due PMs") are icon tools in
`<pems-list-tools>` or items of a `<pems-list-menu>`.

A page with a second list (for example a history under a queue) puts a second `<pems-list-panel>` in the same
`<pems-list-page>`: the panels share the height and each table scrolls inside its own panel.

Row and cell classes are shared too:
- `lp-row`;
- `lp-no` and `lp-subject` for the record number link;
- `lp-actions`, `lp-act` and `lp-act--always` for the row actions;
- the `vs-pill` / `vs-chip` badges.

Column resize needs `data-col-key` / `data-resizable` on the `th` elements; the script starts it for every
`table.lp-table[data-grid-key]`. No page script is needed for search, autocomplete, KPIs or column widths.

## Waves

| Wave | Pages | State |
|---|---|---|
| W0 | component; Engineering Documents and VDR moved onto it | done |
| W1 | document control: DocumentRegister, DocumentRevisions, Transmittal, TechnicalQuery, MDB, MOC, DocumentWorkflow (DCC is a desk, not a list) | done |
| W2 | CMMS: WorkOrder, WorkRequest, Equipment, FunctionalLocation, Instrument, Tag (letter tags), MaintenancePlan, MaintenanceStrategy, JobPlan, SparePart, Permit, Inspection, Finding, Asset | done |
| W2b | the other single-table CMMS lists: CriticalityAssessment, EquipmentCriticality, MechanicalEquipment, FailureModeCatalog, ShiftHandover, RcmFmea, EquipmentFunctionalLocationAssignment | done |
| W3 | procurement and warehouse: PurchaseOrder, Tender, MaterialRequisition, Vendor, Contract, ProcurementPackage, InventoryTransaction, StoreRequisition, IrnReceipt, Replenishment, MaterialItem, PartCatalogueRequest (not lists: Inventory dashboard, InventoryMasterData and PartCatalogueAdmin admin pages, MrsImport, the PreProcurement cartable) | done |
| W4a | users, roles and base data: Users, Roles, Department, Discipline, Employee, AuditLog, SecurityEvents, AuthorizationAudit | done |
| W4b | Administration settings lists: SoDPolicy, StorageProviders, StorageRouting, WorkflowManagement, Help, PurchaseOrderAuthority (not lists: NavigationMenu / NavigationVisibility editors, PermissionMatrix, Organization tree, OrganizationImport, DatabaseUpdate, License, ProjectIntelligence) | done |
| R1 | RelatedList: JobPlan details, and the inline row editors ContractApprovalAuthority, Numbering, RoutingRules | done |

Not converted, because they are not record lists: dashboards, import wizards, AI tools and reports (in CMMS also
Reliability, MaintenanceMasterData, MaintenanceCalendar, EquipmentClassification and the Crew cards). The Letter list
keeps its own richer frame (folders, preview, bulk actions) but uses the same stylesheet.

## Tests

- `ListPageComponentTests` checks each tag on its own: texts, optional tools, badge, chips, empty state and accessibility.
- The page tests (`EngineeringDocumentsListLookTests`, `VdrIndexLayoutTests`, `DocumentControlListPagesTests`, `CmmsListPagesTests`) render the real views.

## RelatedList (child collections of a detail page)

A detail page shows its child records (tasks, lines, rules) in a `<pems-related-list>` section:

```cshtml
<pems-related-list id="jpTasks" icon="bi-list-ol" title="@Localizer["..."]" count="@Model.Tasks.Count"
                   empty="@(!Model.Tasks.Any())" empty-text="@Localizer["..."]">
    <pems-related-table> <thead>...</thead> <tbody> ... row ...
        <pems-related-remove action="@Url.Action("RemoveTask")" key-name="maintenanceTaskId" key-value="@t.Id.ToString()"
                             parent-name="jobPlanId" parent-value="@Model.JobPlanId.ToString()" label="..." confirm="(optional)" />
    </tbody> </pems-related-table>
    <pems-related-add action="@Url.Action("AddTask")" parent-name="jobPlanId" parent-value="..." submit-label="...">
        <div><label class="lp-lbl" for="x">...</label><input id="x" name="..." class="form-control form-control-sm" /></div>
    </pems-related-add>
</pems-related-list>
```

A table edited in place puts `<pems-related-row-form id="row_1" action="...">` (hidden keys as child content) in the first
cell, `form="row_1"` on each input of the row, and `<pems-related-save form-id="row_1" label="..." />` in the action cell.
The forms carry the antiforgery token. Rows edited in a modal keep the modal until SCRUM-482.

Waves: R1 JobPlan details, Numbering, RoutingRules, ContractApprovalAuthority (done); R2 Tender, ProcurementPackage and
IrnReceipt details (done); R3a PartCatalogueRequest Edit and RcmFmea ClassDetail tables (done); R3b Permit details (done); R3c VDR details (done); R3d WorkOrder details (done). All RelatedList waves are done.
A page with its own section design (form pages) keeps its sections and takes only `<pems-related-table>` / `<pems-related-remove>`.

## InfoGrid (label / value block of a detail page)

The block of labels and values at the top of a detail page (code, dates, people, contact) is a `<pems-info-grid>`:

```cshtml
<pems-info-grid>
    <pems-info-item label="@Localizer["Vs_VendorCode"]" mono>@Model.VendorCode</pems-info-item>
    <pems-info-item label="@Localizer["Ed_Revision"]" num>@Model.RevisionNo</pems-info-item>
    <pems-info-item label="@Localizer["Vs_ContactPhone"]" ltr>@Model.ContactPhone</pems-info-item>
    <pems-info-item label="@Localizer["Vs_Overdue"]" tone="@(Model.Overdue > 0 ? "danger" : "")">@Model.Overdue</pems-info-item>
    <pems-info-item label="@Localizer["Ed_Description"]" wide>@Model.Description</pems-info-item>
</pems-info-grid>
```

It renders a description list (`dl` > `div` > `dt` + `dd`) in a responsive grid. The value is the page's own markup (links,
badges, conditions); an empty value shows `empty-text` (a dash). `num` and `ltr` put the value in a left-to-right run so it
stays at the start edge of a Persian page. `layout="rows"` puts each label beside its value (side panels, long technical
lists; label over value at phone width); `value-class` keeps a page's own hook on a value. Styles: `pems-ui.css` (`pems-info*`), loaded by the layout. The ratchet pattern
`info-grid` counts hand-written `vs-info__label` and `<dt>`.

Waves: I1 user, engineering document, equipment, VDR and vendor details (done); I2 the `<dl>` pages (PartCatalogueRequest,
PreProcurement, PurchaseOrder and Task details, rows layout) (done); I3 procurement and warehouse headers (PurchaseOrder, Contract,
Tender, MaterialRequisition, MaterialItem) (done; a subtitle line such as the IRN header stays text, quantity tiles wait for KpiTile);
I4 JobPlan, MaintenanceStrategy, Permit, WorkOrder, WorkRequest and Transmittal headers (done). All InfoGrid waves are done;
the asset, reliability and production run quantity tiles wait for KpiTile.

## KpiTile (one number with its label)

A number with its label (report headers, dashboards, detail pages) is a `<pems-kpi-tile>`; the page keeps its own grid
columns around it:

```cshtml
<div class="col-md-2 col-sm-6">
    <pems-kpi-tile icon="bi-tools" color="amber" label="@Localizer["Open"]" href="@Url.Action(...)" hint="...">@Model.OpenCount</pems-kpi-tile>
</div>
```

It renders the kpi-card markup (styles in `admin.css`: `.kpi-card`, `.kpi-icon`, `.kpi-value`, `.kpi-label`, `.kpi-sub`,
`.bg-icon-*`). `color` is blue, green, amber, red, purple, slate, orange, teal ... or a full `bg-icon-*` class; no `icon` means no
coloured square. `compact` makes a small tile for a detail page; `tone` (danger, success, warning) colours the value.
A tile built in code passes a `KpiCardViewModel`: `<pems-kpi-tile model="kpi" />` (title, count with a
dash for zero, icon, colour, link, hint; an attribute on the tag wins). The ratchet pattern `kpi-tile` counts hand-written
`class="kpi-card"`.

Waves: K1 the six report pages (done); K2 Discipline details, EngineeringDashboard, EngineeringDocumentIntelligence and OCR,
and the four `_KpiCard` callers (done; the partial is retired, `KpiCardViewModel` stays as the data of a tile built in code); K3 the detail-page tiles (Asset,
Reliability, Replenishment, ProductionRun) with the compact look (done); K4 the dashboards with their own large-number tiles (Inventory, AIInsights, DCC,
EquipmentClassification, QC, QC KPIs, GapReport, CBM, reliability DataQuality and Simulate) (done). All KpiTile waves are done.
The executive KPI dashboard (`pems-kpi-tile` CSS class with status colours) keeps its own design.

## Skeleton (loading placeholder)

Pages are rendered on the server; the wait is in the panels a script fills after the page loads. Such a panel shows a
skeleton until its data arrives:

```cshtml
<div id="helpBody"><pems-skeleton kind="card" label="@Localizer["Loading"]" /></div>
```

```js
PemsSkeleton.show(panel, 'table', { rows: 3, cols: 2 });   // label: <body data-pems-skeleton-label> unless opts.label
panel.innerHTML = PemsSkeleton.html('text', { lines: 8 });
```

Kinds: `text` (lines, the last one shorter), `title`, `card`, `kpi`, `table` (rows x cols), `form` (rows of label + field).
One markup (`PemsSkeletonMarkup` in PEMS.UI) for the tag helper, the script and the Blazor `PemsSkeleton`; styles in
`pems-ui.css`. A busy button keeps its spinner (`pems-spinner`, SCRUM-53).

Waves: S1 the component, help drawer, global search, letter preview, position org chart (done); S2 the panels that load content
(org chart, cartable thread replies, letter distribution groups, part catalogue duplicate search and technical class) (done;
the panels that only save keep a busy spinner);
S3 the ratchet pattern `loading-text` ("Loading..." text as a
placeholder; a skeleton label is not counted; baseline zero) (done). All Skeleton waves are done.

## Chart (one chart host)

The page describes the chart with a `BkChartSpec` (`MonthlyBars`, `HBars`, `Donut`, `Gauge`, `Line`; colours from the
`--pems-chart-*` tokens through `BkChartSpec.Primary` ...) and writes `<pems-chart spec="@spec" />`. The Bklit island in
`dist/dashboards/dashboards.js` draws it; a visually hidden table (a line for a gauge) carries the same numbers for screen
readers and without script. The page loads the bundle once with `<partial name="_BkChartStyles" />` and
`<partial name="_BkChartScripts" />`.

Waves: C1 the tag helper, the report pages, KPI, letter cartable report and part catalogue reports (done); C2 a `line` kind
in the Bklit island and the reliability trend page, Chart.js retired (done; a new kind is added in
`frontend/dashboards/src/islands` and the bundle rebuilt with `npm run build`); C3 the home donut (done; `_DonutChart`, `pems-donut.js`
and `pems-charts.js` removed: one chart engine); C4 the ratchet
pattern `chart` (baseline zero) (done). All Chart waves are done.

## TreeView (a hierarchy)

```cshtml
<pems-tree label="@Localizer["Ct_Tags"]" toggle-label="@Localizer["Ct_ToggleBranch"]" remember-key="pems.cartable.tagsCollapsed">
    <pems-tree-item label="@t.Name" key="@t.TagId.ToString()" href="@Url.Action(...)" current="@(t.TagId == currentId)" link-class="ct-tag">
        <pems-tree-label>...the page's own label markup...</pems-tree-label>   @* optional *@
        ...nested <pems-tree-item>s...
    </pems-tree-item>
</pems-tree>
```

A recursive local function that writes tree items must be `async Task` (Razor rule for tag helpers), and the recursion
stays in one view: a `<pems-tree-item>` in a separate partial does not run inside its parent and is not counted as its child. The tree is a WAI-ARIA
tree; `pems-tree.js` (loaded by the layouts) opens and closes branches, remembers the collapsed ones under `remember-key`,
never collapses a branch holding the `current` item, and gives the tree keyboard. Styles: `pems-ui.css` (`pems-tree*`); a page
may set only its colours (as the cartable side panel does).

Waves: T1 the component, the organization positions tree and the correspondence tag tree (done); T2 the help node tree (done; the
navigation visibility editor stays a `<details>` disclosure: its nodes hold forms, which a tree item may not); T3 a ratchet pattern for hand-written trees.
