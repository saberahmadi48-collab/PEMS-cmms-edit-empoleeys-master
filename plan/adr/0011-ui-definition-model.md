# ADR-0011: UI definition model: pages, component instances, data sources and actions are definitions

- **Status:** Accepted
- **Date:** 2026-10-28
- **Decided by:** Hamid Maleki (owner, Member A): approved the batch and the direction, and accepted this ADR on 2026-10-28
- **Plan rows:** #430 (SCRUM-430, this ADR); builds on ADR-0010 (#338); used by #433, #434, #437, #438, #439, #440, #44, #57, #20, #387
- **Sources:** [METADATA_REGISTRY_DESIGN.md](../../docs/architecture/METADATA_REGISTRY_DESIGN.md); ADR-0007 and ADR-0008 (components, texts as inputs); owner message 2026-10-28 ("all pages and components and forms creatable and editable by the UI, changing the database")

## Context

- 303 pages are hand-written Razor views. `PEMS.UI` components already take named-property options (records) and `TranslatableText`; descriptors (`FormDefinition`, `ViewDescriptor`) already deserialize from JSON and render through the component renderer.
- Nothing defines a **page** or a **component instance**, or how a component is wired to data and behaviour without code.
- Owner decision 2026-10-28: differences between installations are handled by migrations (not by per-installation divergence).

## Decision

We add UI kinds to the ADR-0010 envelope. A **page definition** is a layout of regions; each region holds **component instances**. All are JSON payloads inside definition versions.

1. **Component instance** = `{ "id": "...", "component": "<registry key>", "options": { ... }, "visible": "<expression>", "bindings": { ... } }`. `options` must validate against the JSON Schema in the component manifest (#433) generated from the options record; `component` must be a registered key (`button`, `empty-state`, `table`, ...). Text values inside `options` are `TranslatableText` maps.
2. **Page** = `{ "layout": "<layout key>", "regions": { "<region>": [ instance, ... ] }, "route": "...", "permission": "..." }`. Routes of database pages live in a reserved prefix and never shadow a code route.
3. **Layout** is a definition of its own (regions with names and rules), so layouts can also be changed from the UI; the shipped layouts (list page, form page, detail page, dashboard page; #325) are `System` definitions.
4. **Data source** = `{ "kind": "entity-query | saved-view | endpoint | static", "target": "...", "parameters": {...}, "paging": {...}, "filters": [...] }`, resolved on the server through the REST API with row-level security (#437, ADR-0009).
5. **Action** = `{ "kind": "navigate | call-endpoint | workflow-action | open-dialog | refresh", "target": "...", "confirm": {...}, "permission": "..." }`. Actions are data, never script; there is no free-form code in a definition.
6. **Menu, theme, form, view, dashboard, report** are further kinds in the same envelope with their own schemas.
7. **Coexistence with static pages.** A request first looks for a code route; a database page is served only on its own route. A database definition may *override* a shipped (`System`) page by key; removing the override (rollback or reset) restores the shipped one. The code default always exists.
8. **Runtime.** One generic `PageHost` loads the published page definition (cached per ADR-0010), checks the page permission, evaluates `visible` expressions (#15), resolves bindings, renders every instance through the PEMS.UI component renderer in the request's language, and answers 403/404 as REST does. Rendering is server-side; the designer preview uses the same renderer.
9. **Validation at publish.** Schema validation of every instance, existence of referenced components, data sources, actions and permissions, no reference to something the publisher may not read, size and depth limits (design document section 6). References are recorded in `DefinitionReferences` for "where is this used".
10. **Installations.** System pages, layouts and forms are delivered identically by migration/seed; customer definitions are exported and imported as signed packages (#23). No per-installation divergence is designed beyond that.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Keep hand-written views and only make forms dynamic | Does not meet "all pages and components editable by the UI". |
| Store pages as Razor text edited in the browser | Runtime compilation, security and offline-packaging problems; no schema validation. |
| Free-form JavaScript in actions | Unsafe and untestable; the fixed action kinds cover the use cases; extension code goes through the SDK (#94). |
| A separate page table set from forms and views | Duplicates versioning, publish and audit; the envelope already solves them. |

## Consequences

- Good: designers (#439) become API clients of the definition store; the same renderer serves static, database and preview rendering; components stay free of data access.
- Cost or risk: the manifest and schema discipline (#433) must be in place before the designer; expression and visibility evaluation must be safe and fast; layouts and regions need a small stable vocabulary.
- Follow-up work (plan rows): #433, #434, #437, #438, #439, #440; #85 classifies which of the 303 pages are definable.

## Amendments

None yet.
