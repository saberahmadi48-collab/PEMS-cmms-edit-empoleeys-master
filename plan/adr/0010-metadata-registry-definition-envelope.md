# ADR-0010: Metadata registry: one definition envelope with versions, publish and rollback

- **Status:** Accepted
- **Date:** 2026-10-28
- **Decided by:** Hamid Maleki (owner, Member A): approved the batch and the direction, and accepted this ADR on 2026-10-28
- **Plan rows:** #338 (SCRUM-25, this ADR and the design document), #10, #11, #13, #28; extended by ADR-0011 (#430)
- **Sources:** [METADATA_REGISTRY_DESIGN.md](../../docs/architecture/METADATA_REGISTRY_DESIGN.md); ADR-0001 (hybrid storage); the workflow platform (`WorkflowDefinition`, `WorkflowVersion`); the dynamic sidebar design

## Context

- Rows #10-#28 build a registry of apps, modules, entities, fields and relations; rows #44, #57, #58, #59, #20 and #67 each assumed their own storage for forms, views, dashboards, reports, menus and workflows.
- The owner's goal: everything is creatable and editable from the UI and stored in the database. That needs one place, one lifecycle and one write path, otherwise each designer invents its own save, version and rollback rules.
- Facts in the code: the workflow platform already has a proven version lifecycle (Draft, Validated, Published, Retired, immutable published versions, `RowVersion`); the dynamic sidebar already merges a code-owned catalog with admin-owned fields; `PEMS.UI.Abstractions` descriptors are already JSON-shaped.
- Constraints: schema only through migrations; offline; Persian/English texts as `TranslatableText`; row-level security; owner decision 2026-10-28 that differences between installations are handled by migrations.

## Decision

We store every defined thing as a **definition** in one envelope (`Definitions`, `DefinitionVersions`, `DefinitionReferences`, `DefinitionStamps`) with a kind-specific JSON payload, validated against the kind's JSON Schema.

- Lifecycle `Draft → Validated → Published → Retired`; published and retired versions are immutable; rollback publishes an older version again as a new version number.
- Static code-first entities appear as read-only `System` definitions generated from the EF model; customers extend them only through the JSON extension column (#14).
- Shipped definitions are delivered by migration or seed (`Origin = System`, `ShippedVersionId`); customer edits are a newer published version (`IsCustomized`); upgrades never overwrite a customised definition, they store the new shipped version as a draft with a diff; reset = republish the shipped payload.
- Consumers read published versions through one cached `IDefinitionStore`; publish increments a per-kind stamp; other instances refresh within about 2 seconds; HTTP uses `ETag = ContentHash`.
- Workflow tables stay; a `workflow-ref` definition points to them.
- The write path is the definition store REST API (#434, ADR-0009); publish and edit are separate permissions per kind.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| One table set per kind (forms, views, menus, ...) | Repeats versioning, publish, audit, export and cache logic for every designer. |
| Put definitions in files shipped with the product | Not editable from the UI; no per-customer changes without a deployment. |
| Replace the workflow tables now | High risk for a working module; the envelope can reference them and absorb them later. |
| Relational tables for every field of every kind | Schema churn for each new component option; JSON with a validated schema is the same flexibility with one migration. |
| Allow silent DDL from entity definitions | Unsafe; the planner produces a reviewed migration script instead (design document, open question 5). |

## Consequences

- Good: one lifecycle, one REST surface, one audit and export path; rollback and impact preview for free; matches the owner's migrations-first rule.
- Cost or risk: JSON payload needs schema validation and migration of payload shapes (`PayloadSchemaVersion`); the envelope is a new central table, so its cache and permission model must be right from the first version.
- Follow-up work (plan rows): #10 implements the registry, #430/ADR-0011 adds the UI kinds, #433 manifests, #434 store API, #28 cache, #13 schema planner, #17 audit, #23 packages, #440 governance.

## Amendments

- 2026-10-28 (SCRUM-434, first implementation): (1) the optimistic-concurrency token of `Definitions` and `DefinitionVersions` is an application-managed `Revision` number, not a SQL `rowversion` column, so the same code is testable on the in-memory provider and the ETag is stable; `ContentHash` stays the ETag of reads and packages. (2) The permission matrix has no "Publish" action; publish and rollback use the action `Approve` of the module `Definitions`, edit uses `Edit`, create and import use `Create`. A permission per kind (`Definitions.<Kind>.Edit/.Publish`) is not built yet: the module is granted as a whole (open item for the owner). (3) Drafts may be saved incomplete; kind validation runs at the validate and publish actions. (4) The export/import package is hash-checked; the signature arrives with #23. (5) `PayloadJson` is excluded from the generic audit trail (the version table is the history).
- 2026-10-28 (owner decision, retention): retired definition versions are kept **forever**; no code deletes a `DefinitionVersion`. The other options (keep last N, by age, archive, delta storage, release points, per-kind policy and legal hold) and the rules any later policy must obey are recorded in [METADATA_REGISTRY_DESIGN.md](../../docs/architecture/METADATA_REGISTRY_DESIGN.md) section 9.
