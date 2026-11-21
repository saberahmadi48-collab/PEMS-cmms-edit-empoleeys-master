# Sprint 2 programme: from components to the page engine and the form engine (owner, 2026-11-16)

Sprint 2 ends 2026-11-28. By then the page engine and the form engine read their definitions from the database (ADR-0014). **Owner order (2026-11-16, corrected):** (3) data grid, view types, workflow components (SCRUM-54, 55, 58); then (1 and 5 together) data sources/actions and the form engine (SCRUM-437, 578); then (2 and 4 together) page templates and the W1 pilot (SCRUM-339, 342, 106); then (6) the cutover into the database (SCRUM-579, 417, 52). Only (7) the optimisations and diagnostics (SCRUM-574 to 577) go to the backlog for later.

Original order (each step is an issue; ADR-0014):

| Step | Issue | What | Depends on |
|---|---|---|---|
| 1 | SCRUM-572, 573 (In Review) | JSON definition files, one owner per kind, pipeline contracts | - |
| 2 | SCRUM-39 (#15) | expression language: visible, required, readonly, validation conditions, computed values; safe, no code | 1 |
| 3 | SCRUM-437 | data sources and actions as definitions; the hydrators (options, lookups, current values) | 1, 2 |
| 4 | SCRUM-339 (#325) | page templates as components named by the layout definitions (form, detail, dashboard page, header slots, workflow action bar, audit trail, comments thread) | 1 |
| 5 | SCRUM-438 | PageHost: resolve, authorize, hydrate, evaluate, render, static fallback | 2, 3, 4 |
| 6 | SCRUM-54, 55, 58 | data grid, view types, workflow components as components a definition can name | 4 |
| 7 | SCRUM-342, 106 | W1 pilot pages written as JSON; classify the rest (#85) | 5, 6 |
| 8 | SCRUM-578 | form engine on the pipeline: validation, conditions, messages from the definition | 2, 3, 5 |
| 9 | SCRUM-579, 417, 52 | cutover: files into the tables, ownership switched per kind, engines read the database, cache by stamp | 5, 8 |
| 10 | SCRUM-574 to 577 | optimisations and diagnostics (hydrated-result cache, menu from authorizations, compiled plans, timings) | 5 |

Rules: new things only as definitions; no overlap between files and database; every text TranslatableText fa and en; browser QA stays as checklist lines on each issue.


## Status 2026-11-18

Done and merged: data grid, view types, workflow components (SCRUM-54 slice, 55, 58), uploader (SCRUM-580), form engine and data sources/actions slice (SCRUM-578, 437), the cutover (SCRUM-579, 417): the UI kinds are database-owned, the product's own definitions are the `Definitions/System` package installed by `SystemDefinitionSeeder` at every start (fresh install and upgrade are the same idempotent code; a customer's change is never overwritten). Still open: SCRUM-437 slices (workflow-action handler, entity-list providers, data-grid on a data source), SCRUM-339/342/106 (page templates, W1 pilot pages as JSON), SCRUM-52 (template library: needs an entity-backed action handler and the dynamic entities of #45/#67, so it is not started).
