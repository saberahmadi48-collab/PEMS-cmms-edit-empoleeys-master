# PEMS — Enterprise Barcode System

**Status:** Implemented and verified for a bounded, evidence-based scope (Equipment, MaterialItem,
Warehouse, StorageLocation). No fabricated target types were added.

## 1. Source-level audit (Phase 0)

Before writing any code, the repository was searched for existing Barcode/QR/label/identity
infrastructure. Findings:

| Capability | Existing | Reuse | Extend | New | Do Not Create |
|---|---|---|---|---|---|
| Equipment Identity | `Equipment.EquipmentId`/`TagNumber` | ✅ | | | |
| Material Identity | `MaterialItem.MaterialItemId`/`MaterialCode` | ✅ | | | |
| Inventory / Warehouse | `Warehouse`, `StorageLocation`, `MaterialStock` | ✅ | | | |
| Serial Number | `Equipment.SerialNumber` (flat field only) | ✅ (as data, not a target type) | | | Dedicated Serial/Lot entities — `MaterialItem.IsSerialized`/`IsBatchControlled` flags exist but no child entity backs them yet (explicitly "future, not ready" per the entity's own code comment) |
| Lot/Batch | none | | | | Not invented — same reason as above |
| QR | none | | | New (as one of two symbologies) | |
| Barcode | none | | | New (core identity + rendering) | |
| Token/Identifier | none generic; per-module business keys exist (`TagNumber`, `MaterialCode`, `WorkOrderNumber`, `PersonnelCode`) | ✅ (as target lookups) | | New (`BarcodeIdentity.Code`) | |
| Label Printing | none | | | New (QuestPDF-based) | |
| PDF Generation | `QuestPDF` package, used directly in `LetterController` | ✅ | | | New PDF library |
| SVG Generation | none | | | New (`ZXing.Net` encoding + QuestPDF `.Svg()` embedding) | |
| Document Storage | `StorageProvider`/`StorageRoute`/filestore | not used — barcodes are not documents | | | |
| Authorization | `IPermissionService`/`RolePermission` (View/Create/Edit/Delete/Approve) | ✅ | | | New permission action names beyond the existing 5 |
| Audit | `PEMSDbContext.SaveChangesAsync` `ChangeTracker` interceptor | ✅ | | | Parallel audit table |
| API | No `[ApiController]`/REST layer anywhere in the repo — pure MVC | ✅ (adapted, not REST) | | | A new REST API layer |
| Mobile | None found | | | | Not built in this phase (contracts kept future-compatible only) |
| Offline | None found | | | | Not built in this phase |

**Package additions:** `ZXing.Net` 0.16.11 (Code128/QR/Code39 encoding, zero further dependencies)
and `QuestPDF` 2025.7.0 (already used elsewhere in the repo — the reference was extended from
`PEMS.Web` to `PEMS.Persistence` so the rendering service can live alongside every other business
service, per Clean Architecture). No SaaS, no cloud dependency, no native image library.

## 2. Architecture decision (Phase 1)

One shared identity table (`BarcodeIdentity`), one shared service (`IBarcodeService`), one shared
rendering service (`IBarcodeRenderingService`), one shared controller (`BarcodeController`) —
**not** a separate engine per module. A barcode carries only `TargetType` + `TargetId` (resolved
through `Code`, a random, non-guessable token); it never carries business data. This matches the
task's own required architecture:

```
Existing PEMS entities (Equipment, MaterialItem, Warehouse, StorageLocation)
        │
        ▼
BarcodeIdentity (TargetType + TargetId + Code + Status + lifecycle)
        │
        ▼
ZXing.Net (Code128 / QR encoding → SVG)
        │
        ▼
QuestPDF (SVG embedding → PDF label / PNG raster)
        │
        ▼
BarcodeController.Resolve → redirect to the EXISTING Details page of the resolved target
```

## 3. Barcode identity model & stability

`Code` = `PEMS-BR-<16-character random token>` (cryptographically random via
`RandomNumberGenerator`, drawn from a Crockford-like alphabet excluding ambiguous characters for
print legibility). It is generated once and never derived from `TargetId` or any business field —
confirmed by a test that seeds Equipment with a tag containing a marker string and asserts the
generated `Code` never contains it.

Stability is structural, not just a convention: `BarcodeIdentity.TargetId` is an immutable FK-like
integer set once at creation. Changing `Equipment.TagNumber` or `FunctionalLocationId` never
touches the `BarcodeIdentity` row at all — proven by
`GetActiveByTargetAsync_AfterTagChange_BarcodeRemainsSameAndValid` and
`ResolveAsync_AfterFunctionalLocationAssignmentChange_StillResolvesSameTarget`.

## 4. Symbologies (Phase 4)

- **Code128** — primary/default, per the task's own stated preference for Equipment/Material/
  Location/internal identifiers.
- **QR** — offered as the alternative for camera/mobile scanning (compact, works with a phone
  camera, future-relevant per §23 offline/mobile compatibility).
- **Code39 — evaluated, not exposed in the UI.** No existing industrial-equipment compatibility
  requirement was found anywhere in the repository or its documentation; the `ResolveFormat`
  encoder supports it (`"Code39"` symbology string) so it is not a hard architectural block, but no
  UI path generates it today, per the explicit instruction not to implement unjustified formats.
- **GS1 — evaluated, not implemented.** No GTIN/GLN/supply-chain field exists anywhere on
  `MaterialItem` or any other entity, and no GS1 requirement is documented anywhere in the repo.
  Implementing GS1 Application Identifiers now would be speculative complexity with nothing real to
  encode.

## 5. Supported target types (Phase 7/8)

Only entities that genuinely exist and have a real "open this record" destination were wired in:

| TargetType | Entity | Resolve destination |
|---|---|---|
| `Equipment` | `PEMS.Domain.Entities.Equipment` | `EquipmentController.Details` (existing, tab-based) |
| `MaterialItem` | `PEMS.Domain.Entities.MaterialItem` | `MaterialItemController.Details` (existing, tab-based) |
| `Warehouse` | `PEMS.Domain.Entities.Warehouse` | `InventoryMasterDataController.Index` (existing — Warehouse has no dedicated Details page in the current architecture, so resolution lands on the same tabbed master-data screen a user would already use to view it) |
| `StorageLocation` | `PEMS.Domain.Entities.StorageLocation` (the existing Warehouse → Zone → Rack → Bin self-referencing hierarchy — this **is** the "Rack/Bin" concept; no separate Rack/Bin entity was invented) | same as Warehouse |

**Explicitly not implemented as target types** (§9 — "do not invent a parallel entity"):
- **Tools** / **Package / Container** — no such entity exists anywhere in `PEMS.Domain.Entities`.
- **Serialized items / Lot / Batch** — `MaterialItem.IsSerialized`/`IsBatchControlled` are
  forward-looking flags with no backing child entity yet (per the entity's own comment: current
  MRS import data doesn't support this).
- **SparePart** — deliberately excluded. `MaintenanceSpareUsage`'s own code comment states it was
  already switched from `SparePart` to `MaterialItem` in a prior PEMS phase ("SparePart remains for
  compatibility but is not the future growth path"). Barcoding the deprecated entity would create
  exactly the kind of parallel/legacy architecture this task explicitly forbids.
- **Maintenance transactions / Work Order** — not implemented this phase. `IBarcodeService`'s
  `SupportedTargetTypes` set is a simple, extensible allowlist; adding `WorkOrder` later is a
  one-line addition plus a resolve-redirect case, not an architecture change.

## 6. Barcode Resolution (Phase 6) & security (§11)

`BarcodeController.Resolve(code)` is the single, generic resolve endpoint for any client (web
today; the contract is already client-agnostic — see §10). It:
1. Requires `Barcode`/`View` permission (server-side, always — never bypassed because the request
   originated from a scan).
2. Calls `IBarcodeService.ResolveAsync`, which touches only the `BarcodeIdentities` table (a
   3-column projection — no target entity is loaded at this stage, satisfying the performance
   requirement in §28).
3. Rejects `BARCODE_NOT_FOUND` / `BARCODE_INVALID` / `BARCODE_DISABLED` / `BARCODE_REVOKED` with
   explicit, distinct error codes.
4. On success, **redirects** to the target's own existing Details/Index action — it never returns
   the business record directly. That existing action enforces whatever authorization it already
   has today; the barcode grants no additional access. (Equipment/MaterialItem Details pages
   currently have no `View`-level gate of their own — a pre-existing repository condition, not
   introduced or worsened by this work; `Barcode`/`View` is still required to reach `Resolve` at
   all, and every mutating barcode action — Generate/BulkGenerate/Disable/Revoke/Replace/Print — is
   independently gated.)

`Verify(code, expectedTargetType, expectedTargetId)` supports the explicit "wrong physical label"
check from §18: if the barcode resolves to something other than the expected target, the page shows
`EQUIPMENT IDENTITY MISMATCH`. Per the task's own instruction, this only proves the *digital*
identity mismatch — it does not and cannot prove physical attachment.

## 7. Lifecycle (§15) & duplicate protection (§16)

States: `Active → {Disabled, Revoked, Replaced}`. No state transition ever deletes a row —
`RevokeAsync_NeverDeletesRecord_HistoryPreserved` proves this directly. `ReplaceAsync` revokes the
old identity and creates a new one for the *same* `TargetType`/`TargetId`, linking both directions
(`ReplacedByBarcodeIdentityId` / `ReplacesBarcodeIdentityId`) for a traceable chain.

Duplicate protection is enforced at **two** levels, not just the UI:
- Application level: `GenerateAsync` checks for an existing `Active` row for the same target first
  (idempotent — a second call returns the same barcode, proven by
  `GenerateAsync_SameTargetTwice_ReturnsSameActiveBarcode_Idempotent`).
- Database level: a **filtered unique index** —
  `CREATE UNIQUE INDEX ... ON BarcodeIdentities (TargetType, TargetId, Status) WHERE Status = 'Active'`
  — so two concurrent requests racing past the application check cannot both insert an Active row;
  the loser's `DbUpdateException` is caught and it returns the winner's record instead of erroring.
  `Code` itself also has its own separate unique index.

## 8. Bulk generation (§17) & performance (§28)

`BarcodeController.BulkGenerate` reuses the exact Discipline/EquipmentType/Tag filter pattern
already established for Equipment picking elsewhere in PEMS (same "no filter → empty, not the
whole fleet" guard, same 200-row cap) — it does not duplicate or reopen the Bulk Maintenance
Strategy feature; it is a separate action querying the same `Equipments` table for a different
purpose. `IBarcodeService.BulkGenerateAsync` loads all existing-Active barcodes for the requested
IDs in **one** query, builds the to-create list in memory, then issues **one** `SaveChangesAsync`
for the whole batch — never one call per row.

## 9. Rendering (Phase 5)

- **SVG**: `ZXing.BarcodeWriterSvg` encodes directly to SVG markup — the true vector primary output.
- **PNG**: the SVG is embedded into a single-page QuestPDF document and rasterized via QuestPDF's
  own `GenerateImages` API (cross-platform, no `System.Drawing`, no new imaging dependency).
- **PDF label sheet**: `RenderLabelSheetPdf` takes a list of `BarcodeLabelContent` (Code, title,
  subtitle, meta line) — the layout is generic and knows nothing about Equipment/Material; callers
  supply the content, satisfying "do not hard-code the layout per module."

## 10. Authorization (§20)

New, independent `"Barcode"` permission module, seeded with the same role tier used by every other
CMMS/Inventory module in this repository (`MaintenanceEngineer`/`EngineeringManager`:
Create+Delete+Approve, `Viewer`: View-only). **No new permission action names were invented** — the
task's example list (`Barcode.View`/`Generate`/`Print`/`Reprint`/`Verify`/`Disable`/`Revoke`/
`BulkGenerate`) was deliberately mapped onto PEMS's existing fixed 5-action vocabulary instead:

| Task's example permission | Mapped to existing action | Why |
|---|---|---|
| `Barcode.View` / `Verify` | `View` | read-only |
| `Barcode.Generate` (single) / `Print` / `Reprint` | `Create` | writes a new row or increments a counter, same risk tier as every other single-record "Create" in this repo |
| `Barcode.BulkGenerate` | `Approve` | highest-risk action, matching the established convention (`MaintenanceStrategy.BulkCreate`, `EquipmentFunctionalLocationAssignment.Commit`, every import `Commit`) |
| `Barcode.Disable` / `Revoke` | `Delete` | stops an active identity, matching the existing convention (`Discard`/deactivate actions use `Delete`) |

## 11. Audit

No new audit table or mechanism. Every `BarcodeIdentity` insert/update (Generate, Print count
increment, Disable, Revoke, Replace, BulkGenerate) is a normal EF Core tracked change and is
automatically captured by `PEMSDbContext.SaveChangesAsync`'s existing global `AuditLog`
`ChangeTracker` interceptor — the same mechanism every other feature in this session has relied on.

## 12. UI (§21)

A shared `_BarcodePanel.cshtml` partial (model: `BarcodePanelViewModel`) is used from **both**
`Equipment/Details.cshtml` and `MaterialItem/Details.cshtml` — one partial, two real integration
points, not two separate implementations. Both pages already used a tab-based layout (established
by a prior, separate workstream), so a "Barcode" tab was added to each following that exact
existing pattern — no redesign of either page. A dedicated `Barcode` area (`Index`, `BulkGenerate`,
`Resolve`, `Verify`) was added for the workflows that don't belong inside a single resource's Details
page, using the same `content-card`/`table-enterprise`/`btn-pems-solid` classes every other view in
this repository already uses (no new design system).

## 13. Database changes

**One migration, purely additive:** `20260914140235_AddBarcodeIdentity` — creates the
`BarcodeIdentities` table only. No existing table is altered, no column dropped, no data touched.
FKs: 3 to `Employees` (`CreatedById`/`DisabledById`/`RevokedById`, all Restrict) and 2 self-referencing
(`ReplacedByBarcodeIdentityId`/`ReplacesBarcodeIdentityId`, both Restrict — a barcode's history chain
is never cascade-deleted). Two unique indexes: `Code` (global), and the filtered
`(TargetType, TargetId, Status) WHERE Status = 'Active'` index described in §7. Applied to the local
dev database successfully.

## 14. API design (§29)

PEMS has no REST/`[ApiController]` layer anywhere — every existing feature is a standard MVC
controller with server-rendered views and form posts. `BarcodeController` follows that exact
convention rather than introducing a new API style: `GET Barcode/Resolve?code=...` is the generic
"any client that can make an HTTP GET" entry point (usable by a phone browser after a camera scan
today, and by a future native mobile/offline client without changing the contract — see §15), and
every mutating action is a `POST` with `[ValidateAntiForgeryToken]` + a permission check, identical
to every other controller built in this session.

## 15. Future Mobile / Offline compatibility (§23)

Nothing in this phase depends on the caller being the PEMS web UI:
- `Resolve` takes a raw code string and returns either a redirect (web) or, if a future client
  prefers, the same resolution logic in `IBarcodeService.ResolveAsync` can be called from a thin
  JSON-returning action without touching the core service at all.
- The identity model (`Code` → `TargetType` + `TargetId`) is exactly the shape a future offline
  client would cache locally: scan → look up cached `TargetType`/`TargetId` → operate offline →
  sync. No change to `BarcodeIdentity` is needed to support that later.
- QR was included specifically because it is the format a phone camera scans most reliably, ahead
  of any future PEMS Mobile client.

## 16. Test coverage

- `PEMS.Tests/Cmms/BarcodeServiceTests.cs` (19 tests) — identity uniqueness, no business data
  encoded, idempotent generation, unsupported target rejection, no code collisions, stability under
  Tag/FunctionalLocation mutation, resolution (valid/not-found/empty/disabled/revoked), lifecycle
  (disable + re-disable rejection, revoke preserves history, replace chains correctly, print count
  increments, print rejected on disabled), bulk generation (mixed new/existing counts, in-request
  duplicate dedup, unsupported-type all-fail with zero writes).
- `PEMS.Tests/Cmms/BarcodeRenderingServiceTests.cs` (5 tests) — SVG output for both symbologies is
  real markup, PNG output has a valid PNG signature, single and multi-label PDF sheets produce valid
  PDF output.
- `PEMS.Tests/Authorization/BarcodeControllerAuthorizationTests.cs` (14 tests) — every action's
  permission gate, including proof that `BulkGenerate` rejects a role with no module permission at
  all and that `MaintenanceEngineer` (which has `Approve`) passes through to real logic.

Also fixed (required for the existing test suite to keep compiling after adding a new required
constructor parameter to two existing controllers): `EquipmentControllerAuthorizationTests.cs`'s
`EquipmentController` instantiation now passes a real `BarcodeService`.

**Results:** targeted Barcode run 38/38 passed; full regression **1324/1324 passed** (1286
baseline + 38 new, exact match — no other test count changed).

## 17. Known limitations

- Only 4 target types are wired in (Equipment, MaterialItem, Warehouse, StorageLocation). Extending
  to WorkOrder or any future entity is a small, additive change (one `SupportedTargetTypes` entry +
  one resolve-redirect case), not an architecture change — but it was not done speculatively here.
- Warehouse/StorageLocation resolve to the shared `InventoryMasterDataController.Index` page (their
  only existing view today) rather than an individual Details page, because no such page exists in
  the current architecture; building one was out of scope for "Barcode Core."
- `BulkGenerate`'s filter UI currently covers Equipment only (Discipline/EquipmentType/Tag) — the
  underlying `BulkGenerateAsync` service method is generic for any supported target type, so a
  Material/Location bulk-generate UI can reuse it directly without service-layer changes.
- No physical-scanning hardware/camera integration was built — this phase is the Barcode Core
  (identity, generation, rendering, resolution) per the task's own instruction that a full mobile
  app is out of scope unless the audit found one already existed (it did not).
