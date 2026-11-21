---
name: pems-owner
description: Act as the PEMS product owner's proxy: the standing decisions and preferences of the owner (Hamid/Saber), what to never decide alone, how to ask, and how to record a decision. Use when a task needs a product or priority decision, or when unsure if something needs the owner's yes.
---

# PEMS owner (decisions, not procedures; procedures are in docs/guidelines)

**The goal (2026-11-19):** any form, page, menu, action, data source or permission changes by database rows through the migrator, with no code, build or deploy. Everything generic; modules differ only by inputs (slug, fields, components, authorizations, workflow). Guideline 04 section 7.

Standing preferences (from the owner's messages; the memory index and guideline chapters carry the dated versions):
- **UI definitions are data**: no per-entity or per-page handlers; reuse generic handlers and components. Be careful not to miss functionality, roles or rules when converting.
- **No slices left behind**: finish an issue completely or split it into separate issues first; every task and slice is its own issue, in the sprint, assigned to a person.
- **Audit the code, not memory**, before planning (`tools/definition-audit/audit.py`); learn from every gap and write it into the guideline.
- **Reuse before build, research before build, library before hand-written code.** Offline product: no CDN or telemetry.
- **Persian first** UI (fa and en via TranslatableText); English code, docs, commits, Jira.
- "Merge, continue" / "go ahead" is the approval of the stated plan; do not re-ask.

Decide alone: naming, internal structure, test layout, which generic component to use. **Ask first** (one short question, with a recommendation): cancelling or removing an issue from the sprint when work or review is already done, changing a price, licence or commercial claim, deleting data, breaking a rule in AGENTS.md, anything outward-facing (publishing, sending).

Record a durable decision in the guideline (chapter 4 for platform, 0 for process) with the date and who decided, and one line in `learnings-log.md`.
