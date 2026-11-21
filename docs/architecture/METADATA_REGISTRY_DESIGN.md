# Metadata registry and definition model: design

Status: **Accepted (ADR-0010 and ADR-0011) - 2026-10-28; the open questions in section 8 are still open** (plan rows #338 and #430; [ADR-0010](../../plan/adr/0010-metadata-registry-definition-envelope.md), [ADR-0011](../../plan/adr/0011-ui-definition-model.md)). Accepted by the owner on 2026-10-28.
Scope: how PEMS stores everything that is *defined* rather than coded (data entities and fields, and the user interface: pages, forms, views, menus, themes, data sources, actions), how it is versioned and published, how shipped definitions coexist with customer-made ones, and how consumers cache it.

## 1. Principles

1. **Everything defined is a definition.** One envelope, one lifecycle, one REST surface ([ADR-0009](../../plan/adr/0009-rest-api-standard.md), `/api/v1/definitions`), one place to audit, export and roll back.
2. **Reuse what already works.** The workflow platform already has `WorkflowDefinition` + `WorkflowVersion` (Draft → Validated → Published → Retired, published versions immutable, `RowVersion` concurrency, clone, binding). The dynamic sidebar already syncs a code-owned catalog into a table without overwriting admin-owned fields. The registry generalises both; it does not replace them in this step.
3. **Texts are `TranslatableText`** (ADR-0008). Stored as JSON maps of language slug to text.
4. **Schema changes only through migrations** (rule 6). Definitions are *data*; the tables that hold them are created once by a migration.
5. **Differences between installations are handled by migrations.** Owner decision 2026-10-28. System definitions arrive the same way on every installation (migration / seed); only customer-created definitions differ, and they move between environments as signed packages (#23).

## 2. Definition kinds

| Kind | What it defines | Row | Notes |
|---|---|---|---|
| `app`, `module` | grouping, menu root, entitlement key | #10 | module key matches the licence entitlement (#113) |
| `entity` | a data entity: fields, keys, relations, storage mode | #10, #11, #13 | storage per ADR-0001: real table for user-built entities, JSON column for custom fields on static ones |
| `option-set` | managed value lists with translations | #21 | |
| `page` | layout of regions holding component instances | #438 | ADR-0011 |
| `form`, `view` | data entry and list definitions (`FormDefinition`, `ViewDescriptor` shapes) | #44, #57, #387 | already JSON-shaped in `PEMS.UI.Abstractions` |
| `dashboard`, `report` | KPI/chart layouts and report definitions | #58, #59 | |
| `menu` | navigation tree | #20 | generalises `NavigationMenuItem` |
| `theme` | tokens and brand | #30 | |
| `data-source`, `action` | declarative data binding and behaviour | #437 | |
| `workflow-ref` | pointer to a `WorkflowDefinition` | #67 | workflow tables stay as they are |

New kinds are registered in code with: key, JSON Schema of the payload, the permission needed to edit/publish, and the references it may hold. An unregistered kind cannot be stored.

## 3. The envelope

Tables (new migration, never edited once applied): `Definitions`, `DefinitionVersions`, `DefinitionReferences`, `DefinitionStamps`. The compile-checked sketch is [sketches/DefinitionRegistrySketch.cs](sketches/DefinitionRegistrySketch.cs).

- `Definition`: `Kind`, `Key` (unique per `AppKey` + `Kind`), `AppKey`, `Origin` (`System` | `Customer`), `TitleJson`, `PublishedVersionId`, `ShippedVersionId`, `IsCustomized`, `RowVersion`.
- `DefinitionVersion`: `VersionNumber`, `Status`, `PayloadJson`, `PayloadSchemaVersion`, `ContentHash` (SHA-256 of the canonical payload), `ChangeSummary`, validated/published/retired audit columns, `ClonedFromVersionId`, `RowVersion`.
- `DefinitionReference`: edges (`renders`, `reads`, `calls`, `menu-target`) written at validation; they power the impact preview of the schema planner (#13) and "what uses this component".
- `DefinitionStamp`: one counter per kind for cache invalidation.

### Lifecycle

`Draft` (editable) → `Validated` (schema + references + permissions checked) → `Published` (immutable, the one consumers read) → `Retired` (immutable, kept for history and for instances pinned to it). **Rollback = publish an older version again as a new version number** (history is never rewritten). A published version is never edited; changing it means a new draft cloned from it. Enforced in the service and in `SaveChanges` exactly as for `WorkflowVersion`.

In-flight records (workflow instances, form drafts) stay pinned to the version they started with.

## 4. Static entities and shipped definitions

- **Static entities** (the existing 70+ code-first entities) appear in the registry as read-only `entity` definitions with `Origin = System`, generated from the EF model at startup. Their structure cannot be edited; customers may add fields only through an `entity-extension` definition stored in the JSON extension column (#14).
- **Shipped definitions** (system pages, forms, menus) are delivered by migration or seed as `Origin = System` published versions and recorded in `ShippedVersionId`. The product upgrade rule:
  - not customised: the new shipped version becomes the published one;
  - customised (`IsCustomized`): the customer's published version stays; the new shipped version is stored as a draft with a "upstream changed" flag and a diff, and an administrator merges or ignores it;
  - **reset to shipped** = publish the shipped payload again as a new version.
- This is the same pattern the dynamic sidebar uses (code-owned fields synced, admin-owned fields kept), applied to whole definitions.

## 5. Cache and invalidation contract (row #28)

- Consumers read the **published** version only, through one `IDefinitionStore` that caches `(kind, key) → payload + ContentHash` in memory.
- Publish, retire and rollback increment `DefinitionStamp(kind)` in the same transaction. The instance that published evicts its cache entries immediately; other instances compare the stamp (one cheap query, at most once per second) and evict what changed. Worst-case staleness across instances: about 2 seconds.
- HTTP: `ETag` = `ContentHash`; `Cache-Control: private, no-cache` with `If-None-Match` → 304 (ADR-0009). Static assets that are generated from definitions (for example the component manifest) are named by their hash and may be cached as immutable.
- Responses over 1 KB are gzip compressed.

## 6. Limits and security

- Payload ≤ 1 MB per version; ≤ 500 component instances per page; nesting depth ≤ 12; unknown properties rejected by the schema. (Configurable in `env/*.env`, rule 24.)
- Edit and publish are separate permissions per kind (`Definitions.<Kind>.Edit`, `.Publish`), granted through the access matrix (#60, #178); system definitions can be marked protected and then need a second approver (#440).
- Every change is in the audit trail (#17) with a diff; the definition store REST API (#434) is the only write path.
- A definition can reference only definitions the *publisher* may read (checked at validation), so a page cannot smuggle data the author cannot see; the runtime still applies row-level security to the data itself.

## 7. Relation to existing tables

| Existing | In this model |
|---|---|
| `WorkflowDefinition`/`WorkflowVersion`/`WorkflowBinding` | stay; a `workflow-ref` definition points to them. Their lifecycle is the template for the envelope. |
| `NavigationMenuItem` | becomes the first `menu` definition (#20) behind its feature flag; its catalog-sync rule is the shipped-definition rule above. |
| `PMTemplate`, `LetterTemplate`, `ShiftDefinition` | domain data, not definitions; unchanged. |

## 8. Open questions (with owner)

| # | Question | Owner |
|---|---|---|
| 1 | JSON Schema library and licence for payload validation and the component manifest (#433) | assistant proposes, owner approves (rule: library first, licence recorded) |
| 2 | Is a second approver needed for every system-definition change or only protected ones? (#440) | owner |
| 3 | Maximum payload size and instance counts above are starting values; measure with the page runtime (#438) | assistant |
| 4 | When both an upstream shipped change and a customer change touch the same property, is a manual merge enough or is a 3-way merge tool needed? | owner |
| 5 | Should `entity` definitions drive DDL directly (#13/#14) or only through reviewed migrations? Proposal: planner generates a migration script that is reviewed and applied, never silent DDL | owner |

## 9. Retention of definition versions: decision and the options for later

**Decision (owner, 2026-10-28, SCRUM-434 review):** retired definition versions are kept **forever**. The version table is the only readable history of a definition (the generic audit trail deliberately does not store `PayloadJson`), and a rollback needs the old payload. Nothing in the code deletes a `DefinitionVersion`; row #455 adds a storage report, a size warning and the setting `Definitions__RetainRetiredVersions`, whose only supported value is `forever`. Any other value must stop the start-up with a clear message (fail closed), so nobody believes a policy is active that is not.

Size to expect: a payload is at most 1 MB (`Definitions:MaxPayloadBytes`); real forms and pages are usually 5-50 KB. A definition edited daily for a year is a few tens of MB at most, so "forever" is cheap until proven otherwise; #455 shows when it is not.

### 9.1 Options if keeping everything stops being acceptable

| Option | What it does | Good | Cost / risk | When to choose it |
|---|---|---|---|---|
| A. Keep forever (chosen) | nothing is deleted | full history, every rollback target exists, no code to get wrong | table growth, no upper bound | default; revisit when #455 warns |
| B. Keep the last N retired versions | per definition keep Published + Draft + the last N Retired | simple, predictable size | old rollback targets disappear; version numbers get gaps | a few definitions edited very often |
| C. Keep by age | delete retired versions older than X months, always keeping the protected ones (9.2) | matches audit/legal wording | an old but important version can go | when a records policy states a period |
| D. Archive, then prune | move old payloads to a compressed archive table or file with their hash, keep the version row (number, hash, dates, who) without payload | history of who/when stays; payload restorable on demand | restore tooling needed; a rollback to an archived version needs a restore step | audit wants the trail but size matters |
| E. Delta storage | store old versions as diffs against the next one | big saving for small edits | reconstruction cost and complexity; a damaged chain loses history | only with measured need; probably never |
| F. Keep release points | keep versions that were published inside a promotion package (#23) or a release, plus the last N | the versions that mattered to a customer survive | needs the package registry (#23) and a release marker on the version | when #23 exists |
| G. Per-kind policy and legal hold | policy per kind (for example menus and themes forever, forms last 20), and a hold flag that exempts a definition | fits different risk | more settings, more tests | combined with B, C or D |

### 9.2 Rules every future policy must obey

1. Never delete or archive the **Published** version, an open **Draft/Validated** version, the version the published one was cloned from, or the **shipped** version (`ShippedVersionId`, needed for reset and upstream-change diffs, #448).
2. Never prune a version that something still points to: in-flight records pinned to a version (workflow instances, form drafts, section 3), a package awaiting promotion (#23), a legal hold.
3. Version numbers are never reused; gaps are allowed, so the API, the manager screen (#449) and `ClonedFromNumber` must cope with a missing number (return 404 for the version, show 'archived/removed').
4. A rollback target must exist: the API refuses a rollback to a pruned version with a clear problem+json (`version-pruned`), never a 500.
5. Pruning is a **background job with a dry run**, writes an audit entry per deleted version (definition, number, hash, who started it) and runs in small batches; it never runs inside a request.
6. The `DefinitionReferences` of a pruned version go with it (cascade is already configured), and the `DefinitionStamp` is not touched (published content does not change).
7. A policy change is an ADR amendment and an owner decision, a new migration if it adds columns (archive, hold flag), a setting in every `env/*.env`, and a test that asserts rules 1-4.

### 9.3 Where to change it

- No prune path exists today: `DefinitionService` (PEMS.Persistence/Servises) only adds versions and removes an unpublished draft (`DiscardDraftAsync`).
- The setting is read once at start-up (task #455); add the new values and the job there, in one place.
- Update this section, the amendment in ADR-0010 and the row of the task that implements the policy.

## 10. Changing definitions must be safe (owner requirement, 2026-10-28)

Rules for every kind, built by tasks #457-#460 (not in the code yet; today publish checks the shape of the payload only):

1. **Test first.** A publish is preceded by a compatibility analysis of the new version against the published one (safe / breaking / data-loss, with counts and samples from the real data) and a smoke test that renders or loads the new version with sample data. A failure blocks the publish (422 with the report). Breaking changes need an explicit acknowledgement; data loss is blocked unless a migration mapping exists. (#457, rules per kind: forms in #458.)
2. **Roll back by itself.** The switch to the new version stays atomic. After it, a probe runs against the live definition; if it fails, the previous version is published again automatically (a new version number, change summary `auto-rollback: <reason>`, audit entry). If the version it would return to is also unsafe, it stops and raises an alert instead of looping. (#457)
3. **A retired version is republished only after the same check** against today's data and today's referenced definitions: allowed, allowed with warnings, or blocked with reasons. Data loss can never be overridden. (#459)
4. **A later rule never locks a record.** Making a field required (or tightening a rule) does not invalidate stored records and does not block the people who cannot edit that field. Validation is **state-scoped**: only the fields the current user can edit in the record's current state are judged; the rule is enforced at the step that can edit the field; records that violate the current definition in fields nobody can edit now are listed in a 'needs attention' report, not blocked. (#460, validation engine #356)
5. **Type changes are decided by one matrix** of (old type, new type) verdicts shared by form changes and entity schema changes (#13): widening is safe, narrowing or conversion is data loss or breaking, and a type without verdicts fails the build. (#458)

