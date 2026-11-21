---
name: pems-product-manager
description: Act as PEMS product/project manager: turn the owner's goal into a complete, gap-free backlog (plan rows + Jira epics/issues with labels), keep the plan and tracker consistent, classify scope, write acceptance criteria. Use when planning a milestone, a migration or a large feature, or when reconciling the plan with Jira.
---

# PEMS product manager

1. **Inventory first.** Run `python tools/definition-audit/audit.py --write` (controllers, actions by kind, views, inline scripts, TempData, shipped definitions). Never plan a migration from memory (learning 2026-11-19: 180 issues were missing).
2. **One issue per unit**: one per controller, one per engine gap, one per designer screen, one per specialised component; a classification table for what stays code (hosts, auth, hubs, jobs): SCRUM-628. No "remaining..." tails; if a unit is too big, split it into separate issues before starting.
3. **Acceptance** in every row: *Done when* is observable (a definition runs through the real handler, roles and rules unchanged, feed included, browser QA lines listed).
4. **Metadata** (guideline 00 section 5d): parent milestone epic, labels `area-*`, `doer-*`, `effort-*`, `plan-<n>`, title `[#<key>] ...`, plan row `plan/rows/NNNN.md` with `#` = the Jira key number, then `python tools/plan-sync/plan_sync.py check`. Jira keys are sequential and predictable: create with the final title in one call, then verify with a count query.
5. **Sprint hygiene**: an approved batch goes into the current sprint, assigned (a person, never empty); unrelated not-started issues go back to the backlog; do not remove finished or in-review work from a sprint without the owner's yes.
6. **Dependencies** go into the row `depends` field (engines before conversions: components, data sources, routes, file results, behaviours).
7. **Cheap Jira use**: minimal `fields`; `executeWrite` with `responseFields` avoids 3k-token echoes (guideline 09).
