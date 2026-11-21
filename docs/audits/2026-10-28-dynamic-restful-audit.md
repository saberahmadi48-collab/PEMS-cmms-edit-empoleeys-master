# Plan audit: a fully dynamic PEMS, REST, and front/back separation (2026-10-28)

Requested by Hamid (owner). Scope: audit the backlog against the target "every page, component and form is creatable and editable from the UI and stored in the database", audit it for REST architecture, consider separating back end and front end, then propose the next batch.
Rows 430-441 and the corrections below are the result. Plan changes are the rows in `plan/rows` (then still an Excel workbook, now in `0 - trash/`); Jira keys equal the plan numbers from #430 on.

## 1. What exists today (measured on `main`, 2026-10-28)

| Fact | Value |
| --- | --- |
| MVC controllers in `PEMS.Web` | 147 |
| Controllers marked `[ApiController]` | 0 |
| HTTP verb attributes | 163 GET, 668 POST, **0 PUT / PATCH / DELETE** |
| `OpenAPI`, `ProblemDetails`, API versioning in code | none |
| Controller files returning ad hoc JSON | 22 |
| Antiforgery attributes | 669 (browser form posts with cookie sign-in, ASP.NET Identity) |
| Script files calling `fetch` / `$.ajax` | 24 |
| `PEMS.UI` | static Razor components + partials; options are named-property records; descriptors (`FieldDescriptor`, `ColumnDescriptor`, `ViewDescriptor`, `FormDefinition`) already deserialize from JSON; texts are `TranslatableText` inputs |
| Decisions | ADR-0002 (Razor + separate `PEMS.UI`; designers as embedded JavaScript), ADR-0007, ADR-0008, hybrid storage (ADR-0001) |

So the product is a server-rendered MVC application with RPC-style form posts. There is no REST surface to speak of, and the component layer is already data-shaped (JSON descriptors), which is the good news.

## 2. What the backlog already covers

Metadata registry (#10, #338), field types (#11), generic CRUD/query API (#12), schema planner (#13), formula engine (#15), audit (#17), concurrency (#18), form designer and runtime (#44, #45), view designer (#57), dashboards (#58), reports (#59), module wizard (#56), workflow studio (#67), static-to-dynamic migration waves (#85-#95), component library (#31-#41, #319-#334, #386-#392), governed external API (#195), validation catalog and error contract (#355-#366, #363 = RFC 9457).

## 3. Gaps against the vision

| # | Gap | Evidence | Fix (new row) |
| --- | --- | --- | --- |
| G1 | No shared definition model. Forms, views, dashboards, reports, menus and workflows each assume their own storage; nothing defines a **page** or a **component instance**. | #44, #57, #58, #59, #20, #67 | #430 ADR "everything is a definition" |
| G2 | A designer cannot know a component's inputs. | options were positional before SCRUM-424 | #433 component manifest (JSON Schema generated from the options records) |
| G3 | No page runtime or designer, no way to wire components to data and actions without code. | no row | #437 bindings, #438 page definition + PageHost, #439 page designer |
| G4 | Designers would each invent a save path; no governance for editable-by-UI. | no row | #434 definition store REST API, #440 design-time governance |
| G5 | No REST standard, no inventory, no walking skeleton, no gates. #12 plans `/api/d/{entity}` with nothing to conform to. | section 1 numbers | #431 ADR, #432 inventory, #435 skeleton, #436 gates |
| G6 | Front/back separation is a preference without measurements. | ADR-0002 chose Razor | #441 spike + ADR |

## 4. REST architecture audit

Current state is **level 0 of the Richardson model for almost everything**: one URL per controller action, verbs carried in the URL or the form, state changes through POST, errors as redirects or views. That is correct for server-rendered pages and is not a defect by itself. It becomes a defect when the dynamic platform needs to be driven by designers, integrations and a possible separate front end.

Recommended target (to be fixed by #431):

- Resource-oriented routes under `/api/v1`, plural nouns, no verbs; actions as `POST /work-orders/{id}/actions/approve`.
- Real verbs and status codes; `201` with `Location`, `204`, `202` for long operations, `409/412/428` for concurrency, `422` for validation.
- ETag + `If-Match` for optimistic concurrency (ties #18 and #28), `Idempotency-Key` for creates.
- RFC 9457 problem details with the field-error list of #363.
- OpenAPI 3.1 as the contract, linted and snapshot-tested in CI (#436).
- Paging/filter/sort contract shared with #12; allowed-actions hints so a dynamic UI can hide what the user may not do.
- Browser calls keep cookie + antiforgery; integrations use tokens (#195). One deployable.
- Authorization on every endpoint, enforced by a test (same idea as the orphan-controller test).

Strangler approach: MVC pages stay; the API grows resource by resource (#435 pilot, #434 definitions, then #87 wave-1 entities), and #432 gives the map.

## 5. Front end / back end separation

Do **not** decide yet; decide with numbers. Constraints that matter for PEMS: installs on customer servers, works offline (no CDN), Persian RTL and Jalali, licence protection (#96), cookie sign-in already in place, a very large existing view layer.

Recommendation: **API-first now, separation later if measurements allow.** Build the REST API (#431, #435, #434) so every capability is reachable without a Razor page; keep Razor + `PEMS.UI` for rendering. Row #441 (M2, after the skeleton) builds the same list and form twice (Razor/PEMS.UI vs a small SPA on the API), measures TTFB, TTI, payload and server cost, and records the decision and the trigger that would change it. If the SPA wins, the API already exists; if Razor wins, nothing was wasted.

## 6. Backlog quality findings and corrections

Mechanical audit of 397 rows (now 409):

- **4 dependency cycles**: #36 <-> #80; #309 <-> #310, #311, #312 (parent listed as a dependency of its own children). Fixed by removing #80 from #36 and #309 from the three children.
- **11 dependencies on a later milestone**. Fixed: #38 pulled to M1 (feeds the M1 quality gates #327); #355 no longer depends on #46 (#46 now depends on #355: the catalog feeds the rule builder); #366 and #370 depend on #386 (translatable text contract) instead of #24; #208 pulled to M2 (overlaps #367, merge when #367 starts); #360 and #371 moved to M4; #8 pulled to M1 (needed by #378); #223 and #224 moved to M5 (after migration wave 3); #343 drops its dependency on #184.
- `tools/plan-sync` `check` now **fails on a dependency cycle or a dependency on a later milestone**, so this cannot return.
- **Overlaps left in place on purpose** (noted, not merged, because the rows are owned or started by others): #31 is the umbrella of #322-#325; #32 data grid and #323 DataTable; #34 and #322 form standards; #208 and #367 formatting; #195 (external API governance) and the new #431/#435 (internal standard and skeleton; the scopes are now stated on #195); #44, #334 and #387 (form designer, renderer, hydration; all now use the definition store #434).
- Workbook statuses lag Jira by design (Jira owns the real status); a periodic reconcile report is a candidate task.

## 7. Rows added and changed

New (Jira key = plan number): #430 ADR UI definition model, #431 ADR REST standard, #432 REST inventory, #433 component manifest, #434 definition store REST API, #435 API walking skeleton, #436 API quality gates, #437 data-source and action bindings, #438 page definition + PageHost, #439 page designer, #440 design-time governance, #441 front/back separation spike + ADR.

Scope extended: #338 (registry covers UI definitions), #12 (follows the REST standard), #195 (scope vs the internal standard), #387 (through the definition store), #85 (classify pages and controllers too), #44, #57, #20, #56 (save through the definition store).

Dependency/milestone corrected: #36, #310, #311, #312, #38, #355, #46, #366, #370, #208, #360, #371, #343, #223, #224, #8.

## 8. Candidate next batch (for approval)

Order = unblock most work per unit of effort; decisions first, then one thin vertical slice that proves "dynamic + REST" together, then the designers.

| Step | Row | Why now | Size | Needs SQL Server / browser? |
| --- | --- | --- | --- | --- |
| 1 | #431 REST API standard ADR + guideline | unblocks #12, #195, #434, #435, #436 | M | no |
| 2 | #338 registry design + #430 UI definition ADR (write together) | unblocks #10, #433, #434, #437, #438 | M + M | no |
| 3 | #432 REST inventory of the controllers (done: [REST_INVENTORY_2026-10-28.md](REST_INVENTORY_2026-10-28.md)) | sizes the API strangler, feeds #85 | M | no |
| 4 | #433 component manifest from the options records | first code that makes components designer-ready; testable here | M | no |
| 5 | #435 API walking skeleton, then #434 definition store API, then #436 gates | thin slice: first real REST resource, designers get a save path | L + L + M | tests need a database for #434 (QA line) |
| later | #437 -> #438 -> #439, #440, #441 | the page runtime, the designer, governance, the separation decision | L, L, XL, M, L | browser checks |

In flight and not part of this batch: #425 (waiting for the owner's choice of pages), the form shell #322 and the data display task #323 (Saber), #387 (waits for #430 and #434).

## 9. Decisions needed from the owner

1. Approve the batch (steps 1-4 first, step 5 after the ADRs are accepted).
2. Confirm "API-first now, decide the front/back split after the spike (#441)".
3. Paging default for large lists: offset (simple, matches today) or cursor (stable on big data)? ADR #431 will recommend; the owner decides.
4. Whether database definitions (pages, forms) may differ per customer installation (ties to customer version management and #23 promotion).
5. The Titan component names (not in this repository) if the shared naming table in guideline chapter 02 should be aligned.
