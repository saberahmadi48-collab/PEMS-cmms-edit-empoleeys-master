# ADR-0016: A form field is owned by the platform or by the application

- **Status:** Accepted
- **Date:** 2026-11-06 (owner decision, SCRUM-514 and 515); written 2026-11-21 (SCRUM-798)
- **Decided by:** the owner
- **Plan rows:** #515, #798
- **Sources:** guideline 04 (rows "Data vs code" and "Who owns a field or a row"), `docs/architecture/FIELD_VALIDATION_CATALOG.md`, code `FormOwnership`, `DefinitionService.ShipAsync`, `FieldDescriptor.Owner`

## Context

Forms are definitions (database rows shipped as the System package). A customer edits a shipped form in the designer, and the next product update ships a new version of the same form. Without a rule, one of the two loses: an update overwrites the customer's work, or the customer removes a field that workflows, reports, imports or search rely on. The product is offline and installed per customer, so an update must never fail half way or lose data. Precedent: the sidebar (code-owned rows vs rows an administrator changed).

## Decision

We give every form field one owner. A **platform** field (`"owner": "platform"`) is ours: it is delivered and changed only by product updates and is read-only for the customer in the designer. An **application** field (the default) is the customer's: editable by them and never touched by an update. The words are "platform" and "application"; "business" and "system" are not used.

Rules that follow:
- A customer edit may not change or remove a platform field and may not mark a field of its own as platform (`FormOwnership.Protect`: `platform-field-changed`, `-removed`, `-added`).
- An update merges the new platform fields into the customer's customised form and keeps every application field byte for byte (`FormOwnership.Merge`).
- The customer's data wins: an update that would collide with an application field of the same name (`name-taken`) or retype a platform field to another family of values (`kind-change`) **stops before it changes anything** and reports form, field and reason (the preflight of `DefinitionService.ShipAsync`).
- A customised definition keeps its version; the new shipped version is kept beside it (`GET /api/v1/definitions/{id}/upstream`); reset to shipped republishes the shipped payload (needs `Definitions.Approve`).
- Which fields are platform: the identity fields that workflows, imports, reports and search address a record by. Today the `code` field of the master-data forms (departments, disciplines, maintenance groups, material categories, storage locations, units of measure, warehouses; create and edit) is marked (SCRUM-796). New platform fields are added by a product change only.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| The update always overwrites the shipped form | Loses the customer's edits; customers stop updating. |
| The customer's copy is never updated again | The customer misses fixes and new platform fields; versions drift. |
| Per-field three-way merge with automatic conflict resolution | A silent resolution can lose stored values; the owner wants a stop with a report. |
| A separate "system" and "business" field type | Two vocabularies for one idea; the owner chose platform and application. |

## Consequences

- Good: a customer can adapt a form freely and still take every update; an impossible update is stopped with a readable report.
- Cost or risk: a wrongly marked platform field cannot be removed by the customer; the marks are a product decision and are listed here.
- Follow-up work: the preflight report is shown to an administrator on Database Update and `/health/database` (SCRUM-797); more fields are marked as the batch conversions find identity fields.

## Amendments

- 2026-11-21: the first marks (the `code` field of seven master-data forms) are recorded here (SCRUM-796).
