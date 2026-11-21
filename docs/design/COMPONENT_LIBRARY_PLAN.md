# PEMS component library plan (PEMS.UI)

Status: **proposed, 2026-10-23.** Decisions marked *decided* come from the owner; the rest are recommendations with a
gate. Backlog rows: #319 to #334 (plus the existing #29-#43 of area CMP). Standard it must satisfy:
[PEMS_UI_ENGINEERING_STANDARD.md](PEMS_UI_ENGINEERING_STANDARD.md), [WORKSPACE_DESIGN_SYSTEM.md](WORKSPACE_DESIGN_SYSTEM.md).

Decision record: [ADR-0002](../../plan/adr/0002-razor-with-separate-pems-ui-library.md) (D1, D2, D4 and rule 26; D3 stays open until spike #319).

## 1. Goal

Every visible element of the application is a component from one library: a field, a button, a badge, a table, a
skeleton, an empty state, a page template. Pages become compositions of components, and the future dynamic BPMS (forms,
modules and views built by users with no code and no deploy) renders through the **same** components. No page owns
hand-written form, table or status markup.

## 2. Where we are (static audit of all views, `tools/UiAudit/pattern-audit.py`)

Full data: [UI_PATTERN_AUDIT.md](UI_PATTERN_AUDIT.md), per view: [ui-pattern-audit.csv](ui-pattern-audit.csv).

| Finding | Value |
|---|---|
| Views / pages / partials | 339 / 303 / 36; 0 View Components, 0 Tag Helpers |
| Pages by pattern | list 141, form 61 (+20 small), dashboard 26, table-only 32, detail 10, other 13 |
| Hand-written inputs vs model-bound fields | 2,587 vs 202 `asp-for` |
| Tables / modals / offcanvas / badges | 342 / 108 / 12 / 846, all hand-written |
| Pages with the shared page header / empty state / loading state | 33 / 8 / 3 of 303 |
| Inline `style=` attributes | 632 in 197 views; 89 views with inline `<script>`; 9 with `<style>` blocks |
| Offline rule violations | 4 references to cdnjs.cloudflare.com (pdf.js in `EngineeringDocuments/Details`, `VDR/Details`) |
| CSS | 21 files, 4,554 lines, 642 hard-coded hex colours vs 753 `var(--pems-*)` uses, 65 physical `left/right` rules |
| Existing reusable pieces | `.vs-*` workspace CSS (48 views), `_PageHeader`, `_Pagination`, `_KpiCard`, `_PemsLoadingOverlay`, `pems-ui.js` |

Reading: a design language exists (`.vs-*`, `--pems-*` tokens, Bootstrap) but it is applied by copy-paste, not by a
component API. That is why the same field, table and badge exist in hundreds of slightly different forms.

## 3. Research summary (best practices for ASP.NET Core MVC / Razor)

- **Partial views** are plain templates without behaviour; **View Components** add parameters, logic and unit-testability and
  suit navigation, panels and tables; **Tag Helpers** extend HTML elements and suit repeated element-level patterns, with a
  rendering cost when overused ([Andrew Lock](https://andrewlock.net/dont-replace-your-view-components-with-razor-components),
  [ASP.NET Core docs](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/view-components)).
- **Razor components** can be rendered statically on the server and even outside a request with `HtmlRenderer`
  ([Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-components-outside-of-aspnetcore)).
  In MVC/Razor Pages their known friction is: Tag Helpers such as `asp-page` do not run inside them, parameters are awkward to
  pass from views, and forms inside a static component lack the antiforgery token (Andrew Lock's experiment). The server-state cost
  of interactive render modes is avoided by staying static.
- **Design tokens** as CSS custom properties are the industry basis for theming; **logical properties**
  (`margin-inline-start`, `inset-inline-*`) remove duplicate RTL stylesheets, with explicit handling for directional icons and transforms
  ([Smashing Magazine](https://www.smashingmagazine.com/2022/12/deploying-css-logical-properties-on-web-apps/),
  [WFP design system RTL guide](https://designsystem.wfp.org/how-tos/support-rtl)).
- **Governance:** a component is only done with documentation of its states, an accessibility statement, RTL/LTR examples and
  tests; a living catalog is the contract for developers (plan rows #38, #40, #41).

## 4. Decisions

| # | Decision | Status |
|---|---|---|
| D1 | Keep Razor; build a reusable component library; designers as embedded JavaScript components | *decided* 2026-10-18 (#5) |
| D2 | Separate Razor Class Library **`PEMS.UI`** (components, tokens, CSS/JS, localization, catalog, tests); `PEMS.Web` only consumes it | *decided* 2026-10-23 |
| D3 | **Component technology** - see 4.1 | recommendation; spike #319 run 2026-10-27, result in [ADR-0007](../../plan/adr/0007-component-technology-static-razor-components.md) (accepted 2026-10-27) |
| D4 | Study the whole project first (done: audit), then pilot on the **Admin area** (+ account pages and the shared shell) | *decided* 2026-10-23 |
| D5 | Plan workbook, Jira and developers stay consistent through field ownership and a scripted sync | *decided* 2026-10-23, see [../process/PLAN_TRACKER_WORKFLOW.md](../process/PLAN_TRACKER_WORKFLOW.md) |
| D6 | No CDN or internet dependency in any component (Instructions rule 1) | standing |
| D7 | Components use tokens only (no hex), logical CSS only (RTL), no inline styles, all text through localization keys | proposed rule |
| D8 | A "no new raw markup" ratchet in CI: the audit counts may only go down | proposed rule |

### 4.1 Technology for the dynamic BPMS (D3)

Question asked: which is better for the future dynamic BPMS requirements?

The BPMS requirement that decides it: forms, views and modules are **data** (metadata created by users) and must be
**rendered at runtime from that data**, also for previews inside the designers, exports/PDF/e-mail, and for Domain Packs shipped as
extension assemblies (#94). That needs "choose a component by name or type and give it parameters from a dictionary".

| Option | Fit for runtime, metadata-driven rendering | Fit for the 303 existing MVC pages | Main risk |
|---|---|---|---|
| A. Tag Helpers + View Components + partials | Tag Helpers are compile-time markup, so they cannot be picked by metadata; View Components can (`InvokeAsync(name, args)`) | Excellent: native to MVC, `asp-for`, validation and antiforgery just work | A second rendering path is needed for the dynamic engine, or every field is a View Component |
| B. Razor components (static server rendering) | Excellent: `DynamicComponent` / `HtmlRenderer` render by type + parameter dictionary, anywhere (request, designer preview, e-mail, PDF); later interactive islands without rewrites; components are plain .NET types in assemblies | Good only through a facade: friction listed in section 3 | MVC interop (validation attributes, antiforgery), rendering cost if every table cell is a component |
| C. Blazor interactive / SPA | Excellent for interactivity | Poor: rewrites everything, server circuits or large downloads, offline and RTL cost | Scope and risk; contradicts D1 |

**Recommendation: B as the canonical implementation, with a thin Tag Helper facade for MVC views, over a technology-neutral component
contract.**

1. **Contract first** (`PEMS.UI.Abstractions`, #321): `FieldDescriptor`, `ColumnDescriptor`, `ViewDescriptor`, `FormDefinition`.
   Static pages and the BPMS engine produce descriptors; the renderer maps a descriptor to a component. Because the contract is
   neutral, the decision is **reversible**: the same descriptors can be rendered by View Components if the spike fails.
2. **Canonical components are Razor components** rendered statically (no circuits, no WebAssembly, offline-safe).
3. **MVC facade:** `<pems-field asp-for="...">`, `<pems-table>` and friends are tiny Tag Helpers that render the same component, so
   existing views migrate with a one-line change and keep `asp-for`, validation and antiforgery on the surrounding `<form>`.
4. **Performance rule:** a table renders its rows in one component (plain markup per row), never one component per cell.
5. **Gate (#319, one week):** a vertical slice - field set with validation, a 1,000-row table, a form with antiforgery and file upload -
   rendered both from an MVC view and from a descriptor; compared with option A on binding/validation parity, render time,
   RTL/offline, testability (bUnit vs Tag Helper tests) and extension-pack loading. Row #5 required exactly this comparison but it was
   never run. If B fails a criterion, fall back to A for the failing component class, keeping the contract.

## 5. Component taxonomy (everything is a component)

| Level | Components |
|---|---|
| L0 Tokens | colour, typography (fa/en), spacing, radius, elevation, density, z-index, motion; light/dark/Turquoise/brand themes |
| L1 Primitives | Button, IconButton, Icon, Badge / StatusPill, Chip, Link, Divider, Tooltip, Avatar, Spinner, **Skeleton** (text, rectangle, card, table-row, form, page) |
| L2 Fields | Field (label, help, error, required, read-only, validation) wrapping: Text, TextArea, Number, Money, Password, Select, Lookup/Relation picker, TreeSelect, Date and Time (Jalali + Gregorian), Checkbox, Radio group, Switch, File upload, Rich text, Tags; FormSection, ActionBar, DirtyStateGuard |
| L3 Data display | DataTable (server paging, sort, filter, column picker, bulk bar, states), InfoGrid, KpiTile, Chart, Timeline, TreeView, Calendar, Kanban, Card, EmptyState, ErrorState |
| L4 Overlays and navigation | Modal, Drawer/Offcanvas, ConfirmDialog, Toast/Alert, Tabs, Pagination, Dropdown/Menu, Breadcrumb, Stepper, CommandPalette |
| L5 Templates | PageShell (+ header), ListPage (filter bar + table), FormPage, DetailPage, DashboardPage, WorkflowActionBar, AuditTrail, CommentsThread |
| L6 Pages | compositions of L1-L5, or **definitions** (metadata) rendered by the BPMS engine |

Mapping from today's markup is in the audit CSV (`target` column). Existing rows keep their scope: #30 tokens, #31 core library
(umbrella), #32 enterprise grid (advanced behaviour on top of DataTable), #33 view types, #34 form standards, #35 lookup/tree select,
#36 workflow components, #37 rich text, #38 accessibility, #39 responsive QA, #40 catalog, #41 visual regression.

## 6. Rules every component follows (Definition of Done)

1. Parameters follow one vocabulary (`Label`, `Help`, `Required`, `ReadOnly`, `Size`, `Tone`, `Density`); defaults come from tokens.
2. Accessibility: programmatic label, `aria-describedby` for help/error, keyboard operation, visible focus, AA contrast, no colour-only meaning (#38).
3. RTL and LTR from the same markup (logical properties; directional icons mirrored; identifiers LTR-isolated as in `.vs-num`).
4. No inline styles, no inline scripts, no CDN; JS initialisation and cleanup per the UI standard section 6.
5. Every string is a localization key with `fa` and `en` entries (the existing parity tests apply).
6. Documented in the catalog with every state (default, hover, focus, disabled, error, loading, empty), sample code and a RTL toggle.
7. Tested: render/snapshot tests, a11y check, and a Playwright visual baseline in LTR and RTL (#41).
8. A page that uses it passes the browser audit (`tools/UiAudit/audit.mjs`) with no new findings.

## 7. Delivery plan

| Phase | Content | Backlog |
|---|---|---|
| 0 Study (done) | whole-project static audit, pattern CSV, decisions | #29 (partial), this document |
| 1 Decide and set up | spike + ADR for D3; PEMS.UI project; contract and descriptors; tokens consolidation | #319, #320, #321, #30 |
| 2 Build the core | fields and form shell; data display and skeletons; overlays and navigation; page templates; catalog; quality gates | #322-#327, #31-#36, #38, #40, #41 |
| 3 Pilot wave W1 | Admin area, account pages, shared shell (65 pages) | #328 |
| 4 Waves | W2 CMMS (94), W3 documents/VDR/letters (71), W4 procurement/inventory (36), W5 other (37) | #329-#332 |
| 5 Clean-up | remove inline styles/scripts and CDN references; strict CSP | #333, #42 |
| 6 BPMS | the form/view/module builders render through the same components | #334, FRM/MOD rows |

Wave order follows risk and pattern coverage: Admin first because its 20 controllers share list/form/table patterns with low business
risk; the large domains follow once tokens, fields, table, shell, skeleton and RTL are proven. Each wave ends when its pages pass the
audit with the "no new raw markup" ratchet lowered to the new counts.

## 8. Risks

- Spike shows static Razor components cannot meet binding/validation parity: fall back to View Components behind the same contract.
- Big-bang risk: avoided by waves and the ratchet; legacy pages keep working until their wave.
- Performance of large tables: rows rendered in one component; measure in the spike with 1,000 rows.
- Styling churn: tokens first (#30), then components; no restyle of a page without its wave.
- Capacity: the work is large (4 waves of 36-94 pages); sequencing is by plan priority, not by this document.
