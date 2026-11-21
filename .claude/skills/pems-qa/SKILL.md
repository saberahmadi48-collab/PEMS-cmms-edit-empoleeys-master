---
name: pems-qa
description: Act as PEMS QA: build and run the QA checklist of a merged task (local tests, then browser checks), regression-test bugs first, verify definition-driven screens in the browser, record results. Use when a task moves to In Review, when QAing, or when writing tests for a definition or action.
---

# PEMS QA

- **Checklist** (guideline 07): line 1 is the local test run; every check you could not run is an unchecked line; **browser checks are always lines** (page, action, expected result). Never mark a check done that you did not do.
- **Bugs**: issue of type Bug and plan row first, a regression test that fails before the fix, then PR, QA.
- **Definition-driven work**: test the shipped definition JSON through the real handler (`RunSectionActionAsync` and the `.As(this, actor)` shims in `PEMS.Tests/Cmms`), not only the service; prove roles, guards (`guard`, `sectionGuard`), messages (fa and en), redirects and audit rows are unchanged from the controller it replaces. Add a catalog permission assertion for every operation.
- **Browser**: open the converted page, do the action as each role (allowed, denied, separation of duties), check RTL and LTR, mobile width, console errors. A selector check on source is not enough: inspect the rendered control (learning 2026-11-17).
- **Batch handover**: minimum tests that are really needed (one per rule or branch that could silently break); everything else is an unchecked checklist line; merge nearly repeated tests into theories and shared fixtures at the end of the batch (guideline 05, 07 section 2a).
- **Test budget**: each command within about two minutes; `python tools/test-gates/gates.py` picks the tests; SQL-instance and very large suites go to the sprint QA line (guideline 05). Confirm the new test name appears in the run.
- Known SQL-instance failures unrelated to a change are reported by name, never silently ignored.
