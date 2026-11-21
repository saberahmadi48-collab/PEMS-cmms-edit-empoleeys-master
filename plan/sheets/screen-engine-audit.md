# Screen engine audit (batch 1, 2026-11-19)

Verdicts per issue of the component batch, from the code. "Exists" = verified in the repository; "built" = delivered in this batch.

| Issue | Verdict | Where |
|---|---|---|
| 599 list page | built: `entity-list` takes saved views, export, density, page size, bulk actions | component/core/entity-list.json |
| 600 details page | built: `record-view`, `bindings.record`, `entity-details` | PEMS.UI RecordView, PageRenderer |
| 601 dashboards | built: `each`, kpi-tile `value`, `entity-dashboard`, `bindings.record.map` | component/core/entity-dashboard.json |
| 602 import wizard | composed from existing pieces: file upload action (SCRUM-596), forms, grids, operations; built: `entity-import-review` (staged rows grid + confirm/cancel actions for the session id). Each import controller issue (675, 681, 695, 701, 719, 722, 732, 752, 762 ...) writes its staged-rows data source and its commit operation | component/core/entity-import-review.json |
| 616 page behaviours | exists: page `visible` expressions (with `params.*`), form `visible`/`requiredWhen`/`readOnlyWhen` (server and browser), action-button `confirm`; built: `if`/`each` in component definitions, masks. Cascade selects and computed values live in forms (FormEngine), so an inline script with such logic converts to a form definition; it is listed per controller issue | FormEngine, pems-form-rules.js |
| 617 shared layout and partials | exists: 5 layouts as definitions (list, detail, form, dashboard, print-page built), page shell/hero/breadcrumb components. Table below: each compiled partial dies with its controller or is the host shell (kept by decision) | table below |
| 618 result messages | exists: action result contract (success text, refusal text, redirect, JSON outcome) in the action host; TempData uses leave per converted controller, counted by the ratchet (SCRUM-611) | ActionHostController, definition-ratchet.json |
| 619 chart | built: `bindings.chart` (rows of a data source to a chart spec, permission-checked) and `entity-chart` | PageRenderer, component/core/entity-chart.json |
| 620 to 622, 771 views | built earlier: `entity-views` (cards, board, calendar, timeline, gantt, tree) with a switcher; JSON action posts | component/core/entity-views.json |
| 623 file viewer | built: `entity-file-preview` (preview url from a row property). Annotations are a separate issue | component/core/entity-file-preview.json |
| 624 letter editor, merge fields | built: record-view field `template` with `{{key}}` merge fields. The rich-text editor field exists (pems-rte.js); a merge-field picker inside the editor is a separate issue | RecordViewOptions |
| 625 barcode and print | built: `entity-barcode` (the barcode SVG of the record) and layout `print-page` | component/core/entity-barcode.json, layout/core/print-page.json |
| 626 OCR editor and chat panel | not built: two new components with scripts; separate issues | - |
| 627 FMEA / reliability matrix | not built: an editable matrix component; separate issue | - |
| 56 form component standards | exists: form engine field kinds, sections, columns, help text, conditional rules, attachments (file kind); built: `mask`, `inputMode` | FormDefinition, FieldDescriptor |
| 374 input shaping | built: `inputMode` (and masks) from the field definition; length, pattern, required and aria attributes already come from the descriptor | PemsField |
| 386 masked inputs | built: `mask` on a field, `pems-mask.js`, server canonical value | FieldMask, pems-mask.js |
| 348 designer rendering | exists: every stored page renders through the component renderer; the designer preview belongs to the designer batch (439, 604, 609) | - |
| 769, 770 grid | exists: pinning, density, saved views, virtualization (fixed row height); 770 needs the 100k-row browser proof | pems-grid.js |
| 772 reschedule | built: calendar drag and Alt+arrow keys post `{ id, date }` to `moveUrl` (an /a/ action); timeline and gantt bars are a separate issue | pems-views.js |

## Compiled partials (617)

| Partial | Decision |
|---|---|
| _Layout, _LayoutEmbed, _AuthLayout, _ValidationScriptsPartial, Error | host shell, kept (permanent static) |
| _SidebarMenu, _DynamicSidebarMenu | replaced by the menu definitions (SCRUM-590) |
| _PageHeader, _PemsResourceHeader, _Pagination | components exist (page hero, pager); gone with their controllers |
| _BkChartScripts, _BkChartStyles | host assets of the chart component |
| _RichTextEditorScripts | host asset of the rich-text field |
| _ContextualHelpButton, _ContextualHelpDrawer, _HelpNodeTree, _HelpStandardBadges, _HelpVisual | Help module (SCRUM-634, 704) |
| _BarcodePanel | replaced by `entity-barcode` (SCRUM-625) |
| _EquipmentIdentity, _RcmErrors | die with Equipment (SCRUM-692) and RcmFmea (SCRUM-744) |
| DocumentNumberFieldViewComponent | field kind to build with the Document issues (SCRUM-683) |
