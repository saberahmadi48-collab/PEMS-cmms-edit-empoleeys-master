# Part Catalogue Request module (Warehouse) — implementation report

## 1. Moved routes (old → new, HTTP 301, GET/HEAD only, query string preserved)

`/StoreRequisition`, `/MaterialItem`, `/IrnReceipt`, `/Inventory`, `/InventoryTransaction`, `/InventoryMasterData`, `/MrsImport`
→ `/Warehouse/<same controller>/<action>/<id>`.
Registered in `PEMS.Web/Routing/WarehouseLegacyRedirects.cs` (fixed controller allow-list, encoded segments, no open redirect). Sidebar title is now **Warehouse (انبار)**; the RBAC module key stays `Inventory` (no data/role migration needed). Import Center "MRS" card points to the new area route.

New: `/Warehouse/PartCatalogueRequest` (list/edit/details/exports), `/Warehouse/PartCatalogueAdmin`, `/Warehouse/PartCatalogueLookup` (AJAX).

## 2. Kept where they were (with reasons)

- **Engineering Material Requisition / MRQ (MaterialRequisition, ProcurementPackage, Tender)** — these are *engineering documents* (per-discipline MR/MRQ in the EPC document register), not stock/requisition transactions. Moving them would break document numbering, revisions and workflow bindings.
- Spare-part master (`SparePart`, CMMS) stays in CMMS: it is an equipment-maintenance asset record; Warehouse links to it, it does not own it.

## 3. Migrations

- `20261007173502_PartCatalogue_Schema` (additive only; applied to the dev DB). No destructive changes.

## 4. Decisions / deviations from the spec (flagged)

| # | Decision | Why |
|---|----------|-----|
| 1 | Module-owned approval engine (`PartCatalogueWorkflowStep` → per-request snapshot steps → append-only approvals) instead of the generic Workflow Platform | The platform cannot evaluate business-data conditions (Cr/Ca, section, new manufacturer, planning A) nor assign individuals. Steps are data-driven and admin-editable. |
| 2 | "Direct manager" and "CEO" live in module tables (`PartCatalogueOrgLink`, approver assignment key `CEO`) | `Employee` has no manager link. Superior = manager of direct manager. |
| 3 | Admin screens work without an Employee link; creating/approving requests needs one | SystemAdmin accounts typically have no Employee record. |
| 4 | Reference-data upload lives on the Part Catalogue Admin page, not in Import Center | Gated by `PartCatalogue.Admin` (action-based), not by Import-Center role sets; documented as a non-import-center upload in `ImportCenterNavigationTests`. |
| 5 | Is Critical / Is Capital derived from the admin-editable Type Designation table | Spec: derived, read-only. |
| 6 | Cancel by the requester needs no comment; cancel/return by anyone else, and any admin override, requires one | Keeps the audit trail where it matters. |
| 7 | Manufacturer and Supplier lists are empty after seed (only `LOCAL`) | The 3 xlsx files provided cover Technical Class, Commodity Group, Product Family. Import the lists from Admin. |
| 8 | Requester can never approve own request (SoD), even as SystemAdmin | EPC practice. |

## 5. Flowchart steps I am unsure about (flagged `NeedsConfirmation`, editable in Admin)

- **Superior manager** step (is it always required, or only above a value?).
- **Maintenance supervisor** — condition `Critical OR Capital` (flowchart label "Cr/Ca").
- **Spare parts specialist** — which Sections need it (seeded from the instruction images; editable per Section).
- **Part cataloguer** registration and the final **Inventory control** step (Planning method ≠ A).

## 6. "Needs confirmation" mappings

- Three commodity groups seen only in the instruction images (not in the xlsx): `10870 Firefighting`, `10880 Health`, `10890 Environment` (Section HSE) — seeded with `NeedsConfirmation = true`.
- Section → Commodity Group 1 seed mapping (9 rows) is inserted only when the table is empty; afterwards the admin owns it. Group 1 → Group 2 mapping starts empty (= all groups allowed).

## 7. Verification performed

- Build: Web + Tests green. Dev DB seed: Sections 23, Steps 8, Technical Classes 4161, Commodity Groups 88 (85 + 3 flagged), Product Families 93, Planning 12, Type Designations 13 — idempotent across restarts and covered by a test.
- Live (Playwright): list, admin, create → number → save → submit → full approval chain → Completed with part number; SoD (own request not approvable); EN + FA/RTL; dark mode; 390 px (no horizontal scroll); Excel (template INWH-PLA-FO-001 Rev.3 filled) and PDF exports download and open; legacy redirects; sidebar links.
- Tests: `PEMS.Tests/PartCatalogue/*` (54 tests: rules, routing planner incl. skip-CEO / Cr-Ca / section / new manufacturer / planning A, validator, service workflow incl. return/cancel/SoD/permissions, seeder idempotency, global-search scoping, legacy URL mapping).

## 8. Known limitations

- Manufacturer/Supplier Excel import expects a single name column; there is no fuzzy-merge UI.
- Lookup endpoints rely on controller-level authorization (no per-field permission).
- Full-suite regression result is recorded in the session report; `MrsImportBatchCapacityTests` is a known timing-sensitive test (passes alone, can fail under CPU contention).

## 9. Searchable-dropdown clipping and FREE FORMAT TEXT -> Technical Class (2026-10-09)

- **Root cause of "half-open" dropdowns:** every card is `.vs-panel` (`overflow:hidden`) and some lookups live in `.modal-body`/`.pc-table-scroll`;
  the option list was an absolutely positioned child of the field, so it was clipped to the card (measured: 72 % of the Technical Class list and 40 % of an
  Admin lookup visible). Keyboard Arrow keys also rebuilt the list, resetting its scroll, and Escape on an open list also closed the parent modal.
  Fix (single shared component `initLookup`, `pems-part-catalogue.js`): the list is rendered in `<body>` (`position:fixed`), positioned from the input's
  viewport rect (flips upward / limits height when there is no room, follows scroll and resize, RTL-aware); active option scrolls into view; stale search
  responses are discarded; Escape only closes the list.
- **Other Warehouse screens:** Inventory, Material Item and Inventory Master Data use native `<select>` only (no custom searchable dropdown), so they are not affected.
- **FREE FORMAT TEXT -> Technical Class:** `GET /Warehouse/PartCatalogueLookup/ResolveTechnicalClass?text=` (same PartCatalogue permission as the other lookups)
  runs `TechnicalClassMatcher` (Application layer, deterministic, no scores). Order: explicit code (`IR#####` anywhere; any reference code in parentheses, e.g. `ELBOW GENERIC(IR25749)`)
  -> class name at the start of the text (comma = space; longest name wins; duplicates or a half-written sub-class => ambiguous) -> keyword candidates (suggestions only).
  Only an unambiguous code/name is applied automatically, and never over a class the user picked by hand; ambiguous results show explicit candidates (also at the top of the dropdown).
  The server still validates `TechnicalClassId` (exists + active) on save; nothing is imported or migrated (reference data = the existing seeded `PartCatalogueTechnicalClass` table).
- Validation: `TechnicalClassMatcherTests` replays all 4161 classes of `TECHNICAL CLASSESS-2026-08-29.xlsx` (code form always resolves to itself; name-only text resolves 4143, 18 duplicate-description classes need an explicit choice, never a different class).
