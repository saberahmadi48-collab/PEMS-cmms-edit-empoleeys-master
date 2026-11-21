# PEMS UI engineering standard (popups, stacking, state, lifecycle, RTL)

Companion to `WORKSPACE_DESIGN_SYSTEM.md`. That document says *what components look like*; this one says *how they must behave*. Every rule below comes from a defect that was **reproduced in a real browser** in PEMS (see `docs/audits/PEMS_LEGACY_UI_DEFECT_AUDIT_2026-10-10.md`). Rule of the house: *fix a shared component once; never patch it page by page.*

## 1. Popups, dropdowns, menus
* **Never rely on `position:absolute` inside a card.** `.vs-panel` is `overflow:hidden`, `.pems-grid-scroll` / `.table-responsive` are `overflow:auto`; an absolutely positioned menu inside them is clipped or covered by the next row / the pagination footer. (Reproduced: Engineering Documents row "⋯" menu, 2 of 6 hit-test points visible.)
* **Bootstrap dropdowns:** the shared default is Popper `strategy:'fixed'` (set once in `pems-ui.js`). Do not add per-page `data-bs-boundary`/`z-index` workarounds.
* **Custom comboboxes / autocompletes:** use the portal pattern (reference: `pc-lookup` in `pems-part-catalogue.js`): render the list in `<body>`, `position:fixed`, coordinates from the input's `getBoundingClientRect()`, reposition on scroll/resize, close when the input leaves the viewport, `role="combobox"` + `aria-expanded/controls/activedescendant`, arrows/Enter/Escape, `mousedown` (not `click`) to pick so blur does not swallow it, and **Escape must not also close a parent modal**.
* **Menu size is part of the contract.** With the `fixed` default a menu is sized against the *viewport*, not its narrow container: every `.dropdown-menu` is capped to `100vw - 16px`; a menu fed by arbitrary text (notifications) must define its own width; topbar menus open as a full-width sheet below 768 px. Always measure geometry at 375/600/768/992/1366 px in EN and FA when touching positioning (a first version of the shared fix passed at 375 and 1366 px and broke at 600-768 px).
* Native `<select>` and `<datalist>` are rendered by the browser outside the page and cannot be clipped — prefer them when search is not needed.

## 2. Overflow and stacking contexts
* A `z-index` only competes **inside its stacking context**. Anything that must overlay the shell (drawers, modals, toasts, lookups) must be a child of `<body>`/the layout root, **never inside `<header class="pems-topbar">`** (it has `z-index:1030`; the Help drawer rendered there lost to the sidebar rail in Persian — 3 of 9 hit points covered).
* Elements that create a stacking context: `position`+`z-index`, `transform`, `filter`, `opacity<1`, `isolation`, `contain`, `will-change`, `backdrop-filter`. Check the ancestor chain before raising a `z-index`.
* Layer map (do not invent values): content `<1000` · sticky table header `2` · Bootstrap dropdown `1000` · sticky/fixed bars `1020–1030` · sidebar `1035–1050` · modal backdrop `1050`/offcanvas backdrop `1040` · offcanvas `1045` · modal `1055` · portal lookups `1056` · global search panel `1060` · loading overlay `2000`.
* Do not remove `overflow:hidden` globally (it provides the rounded card corners) and do not "fix" overlap by escalating `z-index`.

## 3. Form-state preservation
* A row-level action must not discard the user's filters, sort, page or unsaved input. Either update the row in place (fetch + partial refresh) or round-trip the list state (hidden fields / `returnUrl`) so the redirect returns to the same view.
* Never rebuild a form's controls with `innerHTML=` while the user is editing; replace only the region that changed and keep input values/selection/focus.
* Invalid submissions must re-render with the entered values and field-level messages next to the field (`asp-validation-for`), not only a summary.

* **Tabs:** `pems-ui.js` remembers the active tab(s) when a POST form is submitted and re-shows them after the redirect (same path, 20 s window, never over an explicit `#hash`; storage access is guarded). Do not write per-page tab-restore scripts. (Reproduced defect: acting on one of the 13 tabs of Maintenance Master Data returned the user to the first tab.)

## 4. AJAX and partial updates
* Anything inserted after load needs the shared enhancements (accessible names, status icons, table wrapper). `pems-ui.js` observes the DOM and enhances inserted nodes; call `PemsUi.refresh(root)` only for content created outside the observer's reach.
* Use `fetch` with `X-Requested-With`, antiforgery token header, `credentials:'same-origin'`; show a loading state; handle `401/403/500` visibly (never a silent no-op).

## 5. Modal / offcanvas focus and keyboard
* Use Bootstrap modals/offcanvas unmodified: focus moves inside on open, `Escape` closes (unless `data-bs-backdrop="static"` is a deliberate decision), focus returns to the trigger.
* Never nest a modal inside a clipping container; keep it as a direct child of the page root.
* Dropdowns close on `Escape`; keyboard-only users must be able to reach every menu item.

## 6. JavaScript initialisation and cleanup
* Initialise on `DOMContentLoaded` **and** for content added later (see §4); make initialisation idempotent (`dataset.pemsInit`) so a second run does not double-bind.
* Listeners added to `window`/`document` (resize, scroll, outside click) must be removed when the widget closes or is destroyed (pattern in `pc-lookup`: add on open, remove on close).
* No inline `onclick` handlers in new views; no global functions without a namespace.

## 7. Responsive and RTL/LTR
* Use logical properties (`margin-inline-start`, `inset-inline-end`, `text-align:start`). Bootstrap's **offcanvas placement is physical**: `offcanvas-start` is on the *right* in the RTL build — in Persian it sits where the sidebar is; keep drawers outside the topbar stacking context so the sidebar cannot cover them.
* Test every list/detail page at **375 px** and **1366 px**, in **English and Persian**. No page-level horizontal scroll; tables scroll inside their wrapper only.
* Identifiers, dates and codes use `.vs-num` (LTR isolation).

## 8. Shared components
* Reuse: `.vs-*` workspace classes, `_PageHeader` (one `<h1>` per page: **page content must not render a second `<h1>`** — use `<h2>`+), `_Pagination`, `<pems-kpi-tile>` (the `_KpiCard` partial is retired, SCRUM-337), the portal lookup, `pems-ui.js` enhancements.
* New pages must not copy a legacy pattern. If a pattern is missing, add it to the shared layer and document it here.

## 9. Required browser regression checks (`tools/UiAudit`)
Run against a **clone/test database with test accounts** (never production data):
```
set CHROME=<path to Chromium/Chrome/Edge>   (Playwright's Chromium works: %LOCALAPPDATA%\ms-playwright\chromium-*\chrome-win64\chrome.exe)
set BASE=http://localhost:5199  USERS=test-pcat-dm  [LOCALE=fa]  [ONLY=/EngineeringDocuments,/Asset]
node tools/UiAudit/audit.mjs
```
The crawler takes the page inventory from the signed-in role's own sidebar and, at 1366 px and 375 px, reports: clipped/obscured dropdowns (real hit-testing), modals/offcanvas not opening/obscured/without focus/ignoring Escape, JS exceptions, console errors, failed or 4xx/5xx requests, page-level horizontal scroll, `<h1>` count, duplicate headings, duplicate ids and unlabeled inputs. Run it for `LOCALE=en` **and** `LOCALE=fa` before merging UI work; a new finding kind must have a documented explanation or a fix.
Judge UI state by **polling** (`waitFor`), never fixed sleeps: on a loaded machine the first harness version produced false modal findings that vanished with polling.

Static guard tests (`PEMS.Tests/Web/UiStandardTests.cs`) keep the structural rules from regressing: Popper default in `pems-ui.js`, drawers outside `<header>`, one `<h1>` per view, observer present.
