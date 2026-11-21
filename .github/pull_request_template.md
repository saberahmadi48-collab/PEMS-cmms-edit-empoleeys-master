## What and why
<!-- one or two sentences -->

Jira: SCRUM-___    Plan ID (#): ___

## Checklist (Instructions rules 7, 22, 25)
- [ ] I read `docs/guidelines/` (README reading map) and the related Jira issues before starting
- [ ] Existing code/components were reused or modified; any new component was added to the library first
- [ ] Guideline updated (`docs/guidelines/learnings-log.md` or a chapter), or: nothing to add
- [ ] One branch for this task or approved batch; branch, commits and this title start with the Jira key (all batch keys listed here); the branch is deleted automatically after merge
- [ ] Plan row file `plan/rows/NNNN.md` updated (Status via Jira; Evidence/Report written if closing; `plan_sync.py report <id> "..."` does it) and `python tools/plan-sync/plan_sync.py check` passes
- [ ] Supportability Matrix row added or changed
- [ ] CHANGELOG.md entry
- [ ] Tests added or updated; the affected groups pass - or this PR lists the commands I could not run (they become line 1 of the QA checklist)
- [ ] Schema change has a migration (never edit an applied one); new settings are in every `env/*.env` file
- [ ] **Change manifest** (SCRUM-585): this PR declares, in its description, what a customer's update needs: `migration:` (new migration or none), `feed:` (data the migrator feeds or none), `definitions:` (definitions shipped, as files under `PEMS.Persistence/Definitions/System`, or none)
- [ ] **Definition-only** (guideline 04 section 7): a new or changed form, page, menu, action, data source or permission is a definition (no new controller, view, handler or inline script); `python tools/definition-audit/audit.py` shows no new compiled screen
- [ ] New UI uses PEMS.UI components only (no new raw form/table/modal/badge/button/card markup, inline styles or CDN references); I searched the registry and `PEMS.UI/TagHelpers` first, and a component that almost fit was modified (default output unchanged, tests, registry row), not copied
- [ ] `dotnet test PEMS.UI.Tests --filter AdoptionRatchet` passes; if this PR replaced hand-written markup by a component, the baseline `tools/UiAudit/adoption-baseline.json` was lowered in the same PR (`PEMS_RATCHET_RECORD=1 dotnet test PEMS.UI.Tests --filter AdoptionRatchet`)
- [ ] After the merge I move the issue to In Review with the label `qa-pending`; the QA check in the UI (guideline chapter 07) decides Done
- [ ] Before the move to In Review (and again after every failed QA) the Jira issue has the QA checklist comment and the Tests paragraph, checked against this change (guideline chapter 07 section 1a)
