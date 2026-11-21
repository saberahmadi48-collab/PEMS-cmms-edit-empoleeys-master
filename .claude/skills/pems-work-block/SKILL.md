---
name: pems-work-block
description: On any go-ahead in PEMS (continue, next, yes, a task): pull main, audit the whole backlog, batch related tasks, state the plan.
---

# PEMS work block (guideline 00 section 0; token rules: guideline 09)

Run before the first edit of every go-ahead, say in your first message that you did. Cheap by design: one `git`, one JQL, one plan.

1. **Pull.** `git fetch origin && git checkout main && git pull`; `git log --oneline -15` to see what other sessions merged. A dirty tree you did not make is left alone and reported.
2. **Audit the open backlog.** One JQL, minimal fields (never `parent`, `description`, `comment`):
   `project = SCRUM AND statusCategory != Done AND issuetype != Epic ORDER BY key` with `fields: ["summary","status","labels"]`, `maxResults` 50, page on. Read in full only what touches your area or dependency chain (`python tools/plan-sync/plan_sync.py show <id>` is cheaper than Jira). Check each against the code (ratchet `tools/UiAudit/adoption-baseline.json`, greps, merged PRs). One verdict per issue: **go / modify / split / merge / cancel (owner's yes) / blocked**.
2b. **Standard way for each task you start or create** (guideline 00 section 1, 5e): the audit above is the Jira/plan search (modify a neighbour before creating); then web-research best practice and the established name, and write 2 to 5 sources in the "Started by" comment.
2c. **Partial work.** The Jira status wins: an open issue (To Do, On Hold, In Progress) with part of its work on main goes to In Progress and is finished in the batch; a plan row that says Partial while its issue is Done or In Review is not reopened, its remainder goes into a batch or its own issue (guideline 00 section 0.2).
3. **Batch.** Group by area, files, pattern, dependency chain; order small issues first and the huge ones (effort L, schema, a whole engine) last, light tests per slice and the heavy suites once at the end of the batch (guideline 00 section 0.3, 05); a To Do blocker travels with the task it blocks and is done first. One branch per batch, one PR per safe slice, merge each at once. **A finished issue is never held unmerged** (owner decision 2026-11-19): when its tests pass, merge it with the issues it belongs with and move it to In Review; unfinished work stays In Progress.
4. **State the plan** in one message: batches, what the audit changed or proposes to cancel (evidence in a phrase), what is blocked and by what. The go-ahead is the approval; a cancellation still needs the owner's yes.
5. **Start in Jira before the first commit** (skill `pems-jira-handover`), then work. Test with `pems-test-gates`.
6. **Close.** One build and the heavy tests for the whole batch (guideline 00 section 0.3; fix a broken build before anything else); Merge nearly repeated tests of the batch first (guideline 00 section 0.5); Every issue to In Review with a QA checklist, or one line why not. Update the guideline if a procedure was learned (chapter 06).

Never: start the named issue without its blockers and neighbours; merge under an issue that is not In Progress; plan from a stale checkout.
