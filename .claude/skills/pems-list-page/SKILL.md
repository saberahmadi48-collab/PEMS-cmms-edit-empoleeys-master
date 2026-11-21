---
name: pems-list-page
description: Convert or add a PEMS record list view with the pems-list-* components (filters, chips, paging, tests, ratchet, Persian keys).
---

# PEMS list page (docs/design/LIST_PAGE_COMPONENT.md; guideline 02)

Only record lists (search, filters, paging, column picker). Dashboards, reports, calendars, upload wizards and detail pages are not lists; leave them. Never hand-write the table, badge or button: call the components.

**Skeleton** (copy `PEMS.Web/Views/ProductionOrder/Index.cshtml`, filters + paging; `Views/Projects/Index.cshtml`, live search + modal; `Views/Task/Index.cshtml`, tab filter + create popover):
`<pems-list-page texts="@ListPageTexts.From(Localizer)">` > `<pems-list-panel>` > `<pems-list-toolbar heading total>` with `<pems-list-search>` (`live-filter="true"` for a short list without server paging), `<pems-list-tools>` (`<pems-list-filters>` + `<pems-list-filter-select>`, `<pems-list-reset-columns grid-key>`, `<pems-list-new href|modal>`, `<pems-list-menu>`, `<pems-list-popover>` for a short form), `<pems-list-chips>` + `<pems-list-chip remove-href>`, `<pems-list-grid>` > `<pems-list-table grid-key empty empty-text filtered clear-href>` with rows `<tr class="lp-row">` and `<pems-list-footer>` holding `_Pagination` (give it `GridKey` only when the controller honours `pageSize`). Add `@section Styles/Scripts` for `pems-list.css/js`. Keep the page's own columns, row actions, forms and permissions.

**Rules learned:** a filter value that must always be one of N has no `all-text`; a bool filter is a one-item select posting `true`; status colours are `<pems-badge tone=...>` (success, neutral, danger, info, warning, dark); keep `data-col-key` on headers when the grid key is set; every new `Localizer["Key"]` needs an English and a Persian resource entry; no `alert(`, no inline `onclick`.

**Test:** add a case to `PEMS.Tests/Web/CmmsListPagesTests.cs` (`Render<Controller>` + `AssertBuiltFromTheComponent(html, gridKey|null, rows, keepsDataBadges)`; ViewData keys as the controller sets them). Then `PEMS_RATCHET_RECORD=1 dotnet test PEMS.UI.Tests --filter AdoptionRatchet`, `python tools/test-gates/gates.py`, CHANGELOG entry, PR, merge at once; the browser check is a QA line.
