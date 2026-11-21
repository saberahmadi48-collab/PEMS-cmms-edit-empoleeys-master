# ADR-0001: Hybrid storage for user-built entities

- **Status:** Accepted
- **Date:** 2026-10-18
- **Decided by:** Hamid Maleki, project owner and Member A (plan row #4, "Confirmed by PM: Yes")
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #4 (decision), prototype validation in D_DDL / D_SCHEMA; written under #2 (SCRUM-14)
- **Sources:** `plan/rows/0004.md` (Evidence, Prompt, Requirement, Report); [04-platform-direction.md](../../docs/guidelines/04-platform-direction.md)

## Context

PEMS is moving from static MVC modules to a dynamic BPMS in which users create forms, modules and workflows without code or a deploy.
The data of those user-built things has to live somewhere. PEMS runs on EF Core 10 and SQL Server. The row asked for a benchmark of the
three candidate approaches with a small prototype (100k records, filter/sort/report) and for criteria on performance, migration cost,
reporting impact and the effect on RLS and audit.

## Decision

We use a **hybrid** model:

- **Fully user-built entities get real SQL tables**, created through guided DDL.
- **Custom fields on existing (static) entities are stored in one JSON column** on the entity's own table.

The prototype validation of this choice is done in the rows D_DDL and D_SCHEMA; the decision itself stands (Report of row #4).

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| (a) Real SQL tables for everything via guided DDL | Kept, but only for fully user-defined entities; a table per custom field on existing entities is not worth the DDL cost. |
| (b) JSON column plus indexed computed columns for everything | Kept, but only for custom fields on existing entities; whole user-built entities need real columns, keys and constraints. |
| (c) EAV (entity-attribute-value) | Listed as an option in the row; not part of the decision. The row records no further reason. |

The row records the three options and the resulting hybrid; it does not record a per-option comparison, so none is invented here.

## Consequences

- Good: user-built entities behave like normal tables for reporting, indexing and constraints; custom fields on existing entities need no DDL.
- Cost: two storage paths in the metadata and DDL engine; RLS and audit must cover both (Instructions rule 12 applies to every data path).
- Schema changes still go only through migrations (see [ADR-0003](0003-code-first-database.md)); guided DDL must fit that rule.
- Follow-up: benchmark and prototype in D_DDL / D_SCHEMA; the Requirement of row #4 (performance criteria, migration cost, reporting impact, RLS/audit effect) is to be answered there.
