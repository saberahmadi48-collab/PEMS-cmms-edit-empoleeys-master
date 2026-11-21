---
name: pems-test-gates
description: Choose and run the tests for a PEMS change (tools/test-gates/gates.py), keep to the time budget, and list what was skipped as QA lines.
---

# PEMS test gates (guideline 05 "Test budget", 07 section 2b)

**In a batch (10 to 30 issues): only light checks while building** (a compile of the changed project, the exact new test classes when the machine is quiet). **Heavy tests (full suites, SQL, render suites, browser) are deferred to the end of the batch or the sprint** and QA line 1 says "written, not run" until then (guideline 05, owner decision 2026-11-19).

Run every test that fits; leave the very large suites to the end-of-sprint QA. Each task keeps its own QA line, nothing is combined or "covered by" another task.

1. `python tools/test-gates/gates.py` prints, from your changed files: the build of the test project (`dotnet build PEMS.Tests`; the classes run with `--no-build`, so a stale test dll would hide a test that does not compile), `PEMS.UI.Tests`, the guard classes (Persian keys, themes, list pages, API, error handling) and the exact test classes that mention your files. `--run` executes them and stops at the first failure.
1b. **In a batch** use `python tools/test-gates/gates.py --slice --run` after each finished slice (LIGHT: only what changed since the last green run, the exact classes and guard classes, no whole namespace, no API suite) and plain `python tools/test-gates/gates.py --run` ONCE at the end of the batch (HEAVY, tier 2); the very large suites (whole `PEMS.Tests.Web`, `dotnet test PEMS.slnx`, SQL families) only at the end of the SPRINT (tier 3): the gates never run them, list them as end-of-sprint QA lines; one quiet build, one combined test invocation; do not edit compiled files while it runs (guideline 05 "Batch rhythm"). Merge the slice and move its issues to In Review as soon as it is green.
1d. **Minimum tests for the hand-over**: the issue's own new tests and the exact classes that mention the changed files, nothing more; every other check is an unchecked QA line with its command. At the end of the batch merge nearly repeated tests (chapter 00 section 0.5).
1f. **No full build or heavy test without the owner's input** (owner decision 2026-11-19): after the batch is written, report and ask; run the end-of-batch build and tier 2 or 3 only when the owner says so, possibly for several batches in one pass (guideline 05 test budget).
1e. **Build once, at the end of the batch**: no per-slice build; read the slice for compile errors, build only a small project alone when the change lives in it, merge, and list the tests that could not run as `not done - end-of-batch build and run` lines; the end-of-batch build is mandatory and its fixes go first (guideline 00 section 0.3).
1c. **No time: tier 1 only.** Stop a heavy run you started by mistake and write the deferred runs as `not done - end-of-batch run` or `not done - end-of-sprint QA` lines.
2. Budget: about two minutes per command. Filters name exact classes, `FullyQualifiedName~.Class.`; never a word or a namespace (`~Letter` pulls in SQL tests, `~PEMS.Tests.Web` is 7 to 11 minutes).
3. After a layout, view text or resource change the Persian-key test is mandatory: a missing Persian entry for a `Localizer["..."]` key fails `LocalizationKeyCoverageTests`.
4. A failure in markup, a component or a guard is always fixed. A failure that needs SQL Server (names with Sql, LiveDb, Concurrency, Migration, Numbering, Atomicity, Retry, RealSql) is `not run - no SQL Server`; `DynamicSidebarSqlTests` is the known red on main.
5. Write what you did not run on the issue's QA checklist: line d2, `not done - end-of-sprint QA`, with the exact command (`dotnet test PEMS.Tests --filter "FullyQualifiedName~PEMS.Tests.Web"`, `dotnet test PEMS.slnx`, the SQL families, the browser check).
6. Do not wait for what you cannot run: merge, then In Review (skill `pems-jira-handover`).

Recording flags: `PEMS_RATCHET_RECORD=1 dotnet test PEMS.UI.Tests --filter AdoptionRatchet` rewrites the baseline (re-record after merging main). Build once, then `--no-build`.

**Builds of the test projects and test runs happen after the end of the batch with the owner's input**, never on the agent's own initiative (guideline 05, owner decision 2026-11-19).
