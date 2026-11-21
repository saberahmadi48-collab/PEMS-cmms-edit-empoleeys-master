# PEMS Workspace Design System (`pems-workspace.css`, `.vs-*`)

Stylesheet: `PEMS.Web/wwwroot/css/pems-workspace.css` (renamed from `pems-vdr.css`). Razor + CSS only; uses `--pems-*` theme tokens. Logical properties throughout (RTL/LTR).

| Pattern | Markup | Rule |
|---|---|---|
| Page shell | `.vs-page` | No route-path breadcrumb inside the page (the shell provides one). |
| Page header | `.vs-header` / `.vs-doc-head` | Title + one primary action; secondary actions are `.vs-btn`, dangerous ones `.vs-btn--dangerline` or inside a "⋯" menu. |
| Register (list) | `.vs-toolbar` + table | One search box; advanced filters in an offcanvas; active filters as removable `.vs-chip`s; KPI as a `.vs-chips` strip, not cards. |
| Status | `.vs-pill.vs-pill--icon` + tone | Always icon + text + colour. Tone/icon/label come from `DocumentStatusBadge.Tone/Icon/ResourceKey`. Stored status strings are never changed. |
| Revision | `.vs-pill--current`, `.vs-rev-current`, `.vs-rev-superseded` | Current revision is a filled pill with a highlighted row; previous revisions are muted. |
| Identifiers | `.vs-num` | Document numbers, revisions, dates, codes are LTR-isolated in RTL pages. |
| Detail sections | `.vs-section` + `.vs-info` grid | Flat sections and dividers instead of nested cards. |
| Bulk selection | `.vs-bulkbar` | Select rows, then the bar appears with the actions. |

Status vocabulary (Engineering Documents): Draft, Submitted, IDC Review, Checked, Approved, Issued, Rejected, Revise & Resubmit, Client Approved, Archived; revision: Superseded.
Localization keys: `Ed_*` and `St_*` in `SharedResource(.fa).resx` (parity guarded by `EngineeringDocumentsUiTests`).

## Engineering Documents redesign — known gaps (documented, not changed)
- List has no server-side sorting, column show/hide or saved views (needs backend/JS).
- Many workflow actions lack server-side permission checks (view-level buttons only).
- Revision reviewer/approver/effective fields are never populated.
- Client-review badge codes (A–D) do not match stored Approved/Rejected values.
- Workflow has no due dates/SLA.
- `DocumentRegister/Index` and the Create/Edit forms were not restyled in this pass.

## CMMS Integration — Digital Equipment Passport (`Equipment/Details`)
- **Hierarchy:** Tag number is the dominant heading (`.vs-doc-number.vs-num`), then status / criticality / calibration pills (icon + text), name, type · project · discipline · area, then a flat `.vs-info` grid (P&ID, manufacturer/model, functional location, parent, install date, description, child equipment). The old 8-card KPI grid is now a `.vs-chips` strip whose chips switch to the relevant tab.
- **Work Request vs Work Order (semantic rule):** *Maintenance History* = **Work Requests** (`EquipmentPassportDto.WorkRequests`, loaded from `WorkRequests` where `EquipmentId` = equipment or `AssetId` ∈ the equipment's assets, active only). *Executed Work Orders* is a separate **read-only** list (`WorkOrders`). Work Orders are never created from the passport; the only create action is **New Work Request** → `WorkRequest/Create?equipmentId=` (pre-fills equipment; the Work Request module's own permission check and lifecycle apply). A Work Request row links to its converted Work Order when one exists.
- **Permissions:** buttons/links are shown only if the backend `IPermissionService` grants `WorkRequest Create/View` or `WorkOrder View`; the target actions still enforce authorization themselves.
- **Status display:** Work Request statuses use pill + icon + text (`Draft…Converted/Rejected/Cancelled`); stored values are unchanged.
- **RTL:** identifiers/dates use `.vs-num` (LTR isolation). Known gap: most other passport tabs (Documents, Procurement, Inspections, Classification…) still contain English literals not yet localized.

## Page Address / Route Visibility (global rule)
> PEMS must not display raw technical URLs, controller/action routes, or route addresses as a standard page-header element. User-facing navigation uses concise human-readable labels only when navigation context is genuinely useful.

- **Single source:** `Views/Shared/_PageHeader.cshtml` (adapter) over the PEMS.UI partial `_PemsPageHeader.cshtml`. The layout does not render it (removed on owner request, guarded by `GlobalPageHeaderTests`); a page that wants the row calls the partial itself. It outputs ONE compact row: the page title (`ViewData["Title"]`, `h1.pems-page-title`) plus a muted module label (`ViewData["Section"]`, only when different from the title). The former `_Breadcrumb` partial ("Dashboard / Section / Title", ~100 px incl. the duplicate H1) was removed. Nothing reads `Request.Path`/`RouteData` for display.
- **Pages with their own header** (e.g. Engineering Documents detail, Help) set `ViewData["HidePageTitle"] = true`.
- **Do not repeat the title inside the page.** A view must not start with an `<h5>/<h2>` that repeats `ViewData["Title"]`; put only the page actions in the first row (`d-flex justify-content-end`). Where a parent link is genuinely useful, use a local link/back action in that page's action row (not a route-shaped breadcrumb). The sidebar is the module navigation.
- **No large decorative header blocks**; the header is one 28 px row. Guard tests: `GlobalPageHeaderTests`.

## Unified industrial layer (site-wide) — `pems-workspace.css` + `pems-ui.js`
Loaded by `_Layout` for **every** page (after `admin.css`). Rule: *fix a shared component here, once — never patch it page by page.* Uses only `--pems-*` tokens, so light/dark/ocean/warm/midnight all work.
- **Cards:** `.content-card`, `.kpi-card` are flat sections (8px radius, hairline border, no shadow/gradient/hover-lift). Prefer chip strips (`.vs-chips`) over KPI card grids; use cards only when they add hierarchy.
- **Buttons:** one primary colour — `btn-pems-solid` and Bootstrap `btn-primary` are the same flat brand button. `btn-outline-primary/secondary` are neutral outlines. Buttons inside table rows are always quiet outlines (never a column of filled colour blocks); destructive row actions are red text.
- **Status:** every `.badge` (`bg-*`, `text-bg-*`, `badge-status-*`) is one restrained pill; `pems-ui.js` prepends a semantic icon (success ✓, danger ✗, warning ⚠, info ⓘ, progress ↻, neutral –) so status is never colour-only. Counters (numeric) and tab/sidebar badges are excluded.
- **Tables:** compact rows, tabular numerals; tables that are not already in a scroll wrapper get `.table-responsive` automatically (no page-level horizontal scroll).
- **Forms/modals/tabs:** compact labels (12.5px), brand focus ring, one modal structure (8px, hairline header/footer).
- **Accessibility:** global `:focus-visible` ring, `prefers-reduced-motion` honoured; icon-only links/buttons get an accessible name + tooltip (from `title`, else the icon meaning in EN/FA); unlabeled inputs get `aria-label` from the nearest label or field name.
- **Shell:** desktop (≥992px) sidebar is a 64px icon rail that slides open on hover/focus and closes on leave (sub-headings hidden in the rail). Tablet/phone (<992px) uses one direction-aware transform implementation toggled by `.pems-opened` (the old LTR rule keyed to `.show`, so the hamburger did nothing in LTR). The topbar tightens below 576px.
- **Admin area:** `administration-design-system.css` was a parallel design system (blue primary, 12px radius, shadows, gradient hero). It now maps to the same tokens/accent; admin heroes no longer repeat the page title or add decorative blocks.
- **Lists (reference: Equipment, Engineering Documents):** one search (a suggestion opens the record, Enter filters), chip strip for counts, single toolbar row, primary action in the toolbar.
- **Identifiers:** `.vs-num` isolates tags/numbers/dates LTR; on table cells it must stay `display: table-cell`.

## Phase 2 additions
- **Page titles and module labels are localized at one place.** Controllers still set `ViewData["Title"]` / `["Section"]` as English literals; `_PageHeader` (and the `<title>` in `_Layout`) look the English text up in `SharedResource` (English text = key; `ResourceNotFound` -> show the original). Every literal controller title has a Persian entry in `SharedResource.fa.resx` (guarded by `SiteWideUiSystemTests`). New pages: add the title's Persian translation to the FA resx.
- **Forms:** every input has a `<label for>` bound to its `id`; optional fields say "(optional)" via `Ed_Optional`; option *values* (stored data) are never translated, only their display text.
- **Rich-text editor (`_RichTextEditorScripts`)**: the editor is moved inside its field's column (it used to render detached below the card), has an explicit height, and every toolbar control/picker/tooltip input gets a localized accessible name (`Rte_*` keys).
- **Duplicate headings are removed from the markup, not hidden with CSS** (admin hero titles).
- **Lists (Equipment, Work Orders, Assets, Correspondence):** chip strip (status chips are real filters) + one search + one toolbar row; the selected folder/filter is a quiet marker, never a second filled primary.

## Phase 3 additions
- **Language purity rule.** English mode shows no Persian PEMS-owned UI text; Persian mode shows no English PEMS-owned UI text. Business data (tags, document numbers, manufacturer/model, imported titles, master-data names such as project/discipline names, emails, language names shown in their own script) is never translated. Persian-text and English-text resource keys are both allowed: **the English text (or the Persian text) of a literal is the resource key**; the neutral `SharedResource.resx` is English, so a Persian-literal key needs an English value there **and** a self-mapping in `SharedResource.fa.resx`.
- **No literal UI text in views.** Use `@Localizer["..."]`. `LanguagePurityTests` fails when the number of literal UI strings in views grows beyond `docs/design/language-purity-baseline.txt` (a ratchet: lower it when you localize more), and when the EN resource contains Persian values or the FA resource still contains English UI.
- **The accessibility helper must not leak a language.** `pems-ui.js` derives names from labels/titles; it never invents an English field name on a Persian page.
- **Offline.** Runtime JS/CSS libraries are served from `wwwroot/lib` (Bootstrap Icons, Quill, Chart.js 4.4.1/4.4.4, SignalR 8.0.7) at the same versions that were on the CDN. pdf.js remains external (see report).
- **Topbar.** Brand | search | tools never overlap at any width (search flexes and caps at 240px below 1200px).

## Token file (`PEMS.UI/wwwroot/tokens.css`)
The Light and Dark values of the `--pems-*` tokens (brand, surface, text, status, chart, Bootstrap bridge) are defined once in `PEMS.UI/wwwroot/tokens.css`, served as `/_content/PEMS.UI/tokens.css` and linked before `site.css` in every layout. Change a Light or Dark value there. The Turquoise, Ocean, Warm and Midnight blocks are there too (moved verbatim from `site.css`, SCRUM-476). A literal colour in `site.css`, `pems-workspace.css`, `pems-themes.css` or `administration-design-system.css` is a raw palette token `--pems-color-<hex>` declared once on `:root` at the end of `tokens.css` (same value as before; a theme or white-label build overrides the token); a test fails when a literal hex comes back. Still spread over `admin.css`, `pems-glass.css`, `auth.css` and `pems-cartable.css`: later-in-order theme overrides, the typography, spacing, radius and density tokens, and the physical left/right rules (SCRUM-476, remaining).
