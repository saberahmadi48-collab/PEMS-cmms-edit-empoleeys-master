---
name: pems-jira-handover
description: Start a PEMS Jira issue, move it to In Review with the QA checklist, or cancel it, with the fewest Jira calls (guideline 00 and 07).
---

# PEMS Jira steps (cloudId https://atapems.atlassian.net, project SCRUM)

Every Jira write echoes the whole issue (about 3k tokens): one call per issue per step.

**Start** (before the first commit; assignee yourself or the person asked; never touch another person's started issue):
1. `editJiraIssue` once: `fields` = `{ "assignee": {"accountId": <atlassianUserInfo>}, "customfield_10020": <active sprint id> }` (Sprint 1 was id 5; read `customfield_10020.value[0].id` of a sprint issue when unsure). `labels` **replaces the whole list**: send all of them.
2. `transitionJiraIssue` with `transitionName: "In Progress"` and `comment` = "Started" + plan in 3 to 5 lines (batch partners, what you reuse, what you will not do). Verify `sprint in openSprints()` when it matters.

**In Review** (only after the PR is merged into main; the author never moves to Done). Hand over with the minimum tests that are really needed: every other check goes onto the QA checklist as an unchecked line with its command (guideline 00 section 0.3):
1. `editJiraIssue` `labels` + `qa-pending` (full list).
2. `transitionJiraIssue` `transitionName: "In Review"` with `comment` = PR links, "Tests: passed" with commands and counts, "Tests: not run - <why>", and the QA checklist: line 1 the local test run, one line per *Done when*, then every skipped check as `- [ ] n. <check> (not done - <reason>)`: SQL families, `PEMS.Tests.Web` render suite, the browser check (golden path, fa and en, 375 px), manual checks M-n. Name the exact command or page.
3. Work that is only part done stays In Progress with the same checklist as a draft; say which lines belong to which step.

**Cancel** (owner's yes first): `transitionJiraIssue` `transitionName: "Cancelled"` with a comment giving the reason and the evidence (PR, test, ADR, the issue that replaces it). Cancelled sits in the Done category with resolution Done; do not count it as delivery.

**Create** (first the standard way, guideline 00 section 1, 5e: JQL search for the same or a neighbouring issue and modify it if one fits, web research with sources written in the description or plan comment): `createJiraIssue` uses `issueType` (not `issueTypeName`) and `parent` (the milestone epic); then one `editJiraIssue` for the title `[#<key number>] ...`, labels `area-*`, `doer-*`, `effort-*`, `plan-<key number>`, assignee and sprint; then `plan_sync.py new <key number> ...` and `check`.

**Read cheaply:** lists with `fields: ["summary","status","labels"]`, never `parent`/`description`/`comment`; scope from `python tools/plan-sync/plan_sync.py show <id>`. Never `text ~` searches.
