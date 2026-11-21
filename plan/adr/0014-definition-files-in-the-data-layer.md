# ADR-0014: Shipped definitions are JSON files in the data layer, hot-reloaded

- **Status:** Accepted
- **Date:** 2026-11-16
- **Decided by:** Hamid Maleki (owner): files in the data layer with hot reload; texts inline as TranslatableText; page templates are components named by the definition (answers of 2026-11-16)
- **Plan rows:** #572 (SCRUM-572); builds on ADR-0008, ADR-0010, ADR-0011, ADR-0013; Sprint 3 moves the files into database tables (SCRUM-417)
- **Sources:** [METADATA_REGISTRY_DESIGN.md](../../docs/architecture/METADATA_REGISTRY_DESIGN.md), ADR-0011 points 3, 7 and 8

## Context

- The page and form engines must read everything that defines the UI (layouts, fields, validation rules and messages, conditions, exception texts) from data, so that editing the data changes the UI and nothing is left in code.
- The database-backed definition store exists (ADR-0010, `IDefinitionStore`) but nothing ships in it; Sprint 2 keeps the data layer as JSON files, Sprint 3 moves it to tables.

## Decision

1. **A folder of files.** `PEMS.Persistence/Definitions/Files/{kind}/{appKey}/{key}.json` is copied next to the application as `definitions/` (build and publish). The setting `Definitions:Files:Folder` points elsewhere (for editing on a server); `Definitions:Files:FailOnInvalid` (default true) and `Definitions:Files:Watch` (default true).
2. **One envelope per file:** `{ "kind", "key", "appKey", "version", "title": {fa, en}, "description"?, "payload" }`. The path must equal `kind/appKey/key.json`; unknown envelope properties are rejected.
3. **Checked at start-up and at every reload:** the kind is registered and validates the payload (`IDefinitionKind`, the same code the REST API uses), size and depth limits, and **every translatable text carries both `fa` and `en`, not empty** (an object whose keys are all language codes is a translatable text, ADR-0008). An invalid file stops the start (`FailOnInvalid`); at a reload a broken edit keeps the last good version of that key and is listed in `IDefinitionFiles.Problems`.
4. **Hot reload.** A file watcher reloads after 300 ms of quiet; `IDefinitionFiles.Generation` grows with every reload so caches can compare it.
5. **One owner, no overlap (amended 2026-11-16).** A definition lives in exactly one place. Until Sprint 3 that place is the files: the runtime `IDefinitionStore` is the file store and reads nothing from the database. The database definition tables and the `/api/v1/definitions` service stay as they are but are dormant for the UI kinds (no seeder writes to them, no page host reads them). Sprint 3 moves the definitions wholesale into the tables and removes the files in the same change, never both at once.
6. **New kind `layout`:** a tree of plain containers (div, section, header, main, aside, footer, nav, article; classes; `role`, `id`, `aria-*`, `data-*`) with named regions in it; no script, no inline style, no event handlers. Shipped: `list-page`, `form-page`, `detail-page`, `dashboard-page`.

## Ownership and the run-time pipeline (amended 2026-11-16, owner)

The file step is temporary: every new definition is born as a JSON file; definitions that are database data by nature are database-owned from the start; later the stable files move to the database.

**Ownership list, per kind (one owner each, never two):**

| Kind | Owner now | Owner after the Sprint 3 cutover | Notes |
|---|---|---|---|
| `layout`, `page`, `form` (and the next UI kinds: `view`, `dashboard`, `action`, `data-source`) | files | database | born as files; the cutover imports the files into the tables (the export/import package already exists) and deletes those files in the same change, one kind at a time |
| `option-set` (managed value lists) | database | database | database data by nature; created by a seed or migration when missing, never as JSON |
| `menu` | database (`NavigationMenuItems`, synced from code defaults) | database | the menu a user sees is built from the definition plus the user's authorizations (hydration, below) |
| `entity` and field definitions | generated from the EF model | generated | read-only, produced at start-up |
| `workflow-ref` | its own tables (`WorkflowDefinitions`) | same | the workflow tables stay as they are |

`IDefinitionStore` routes by kind to the owner (`RoutedDefinitionStore`); a kind with no owner returns nothing. The map is configuration (`Definitions:Ownership:{kind}` = `files` | `database`), one value per kind, so two owners for a kind cannot be written.

**Run-time pipeline** (every page and form, in this order):
1. **Resolve** the definition (route or key to a published definition, from its owner; a missing or invalid one gives the static fallback page).
2. **Authorize** the page or form permission before any data is read.
3. **Hydrate** the definition for this request: option sets, lookups, current values, defaults, and the parts the user may see (for example the menu is rebuilt from the user's authorizations). Hydration is declared in the definition (data-source bindings, #437), is a pure step that never changes the cached definition, and returns a resolved definition.
4. **Evaluate** conditions and expressions on the resolved definition (#15).
5. **Render** through the engine: layout regions filled with component instances from the registry.

The definition is cached by its content hash and shared by all users; the hydrated result is per user and request. Optimisations (caching hydrated results per permission set, rebuilding menus from authorizations, compiled definitions) are separate issues (SCRUM-574 and following), not part of the first pipeline.

## Alternatives considered

| Option | Why not |
|---|---|
| Embedded resources | Editing needs a rebuild; not "edit the JSON, the UI changes". |
| Database tables now | Skips the files step of the Sprint 2 goal; the admin editor comes with the designers (#439). |
| Texts as resx keys | A JSON edit alone could not add a text; owner chose inline TranslatableText. |

## Consequences

- Good: one place defines the UI; a missing translation cannot start; the same kinds validate files and API writes; no definition can exist in two places; Sprint 3 only changes where `IDefinitionStore` reads from.
- Cost: files are part of the deployment (the pipeline publishes the folder); a hand edit on a server is not audited until the files move to tables (#440).
- Follow-up: expression engine (#15), PageHost and data sources and actions (#438, #437), page templates as components, W1 through JSON (#328), then the form engine.

## Amendments

- 2026-11-16 (owner): "nothing should overlap between the database and the JSON files". The first version layered a published database definition over the file of the same key; that layering was removed (point 5). Open question for the owner: whether the REST write API must refuse the file-owned kinds (layout, page, form) until Sprint 3.
