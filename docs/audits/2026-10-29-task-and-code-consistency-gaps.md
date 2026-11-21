# Task-to-task and code-to-code consistency audit (2026-10-29)

Plan only; no code changed. Follow-up of `2026-10-28-code-vs-plan-gaps.md`. Owner request: look for gaps between tasks themselves and between code features themselves.

## 1. Changes made to the plan

| Finding | Action |
|---|---|
| #10 (registry, M1) and #28 (cache) repeat what #434 and ADR-0010 built (envelope, versions, stamp cache) | #10 now registers kinds on the existing envelope; #28 extends the existing stamp cache; no second set of tables or cache |
| #11 field types are the source of the type-pair matrix also needed by #458 and #13 | #11 states it is the one matrix source |
| Missing dependencies | #12 -> #436; #20, #44, #56, #57 -> #451; #437 -> #431, #433; #438 -> #15, #319; #439 -> #434, #451; #440 -> #60; #451 -> #325, #30, #448; #455 -> #174; #460 -> #60 |
| P0 rows depend on P1 rows | #454, #451 and #432 raised to P0 |
| Group names drifted (12 rows) | Dynamic form builder -> Form builder; Metadata & dynamic core -> Dynamic data core; Persian & localisation -> Persian localization; Quality & testing -> Quality & performance; Reporting & export -> Reporting; Process & documentation -> Support & documentation |
| Old rows still said "workbook" | #280, #336 reworded |
| Code-to-code duplication | Folded into existing rows (below), no new rows |

Not added: #45 -> #460, because #460 already depends on #45 (cycle). `plan_sync.py check`: 435 rows, 0 findings.

## 2. Code-to-code duplication (folded into existing rows)

| Duplication in the code | Row that now carries it |
|---|---|
| Five permission styles: HasPermissionAsync about 522 uses, RequirePermission 22, MenuAccess 33, AuthorizeForRolesAsync 32, IsInRole 14 | #461 (choose one mechanism while reviewing; do not add a sixth) |
| About 20 services with their own numbering code next to NumberingService | #315 (move them over; ratchet test) |
| 17 status-history entities and several audit stores next to the generic audit | #17 (decide which stay as domain tables) |
| Letters have their own approval, routing, delegation and reminder entities and do not use IWorkflowRuntime; other modules (Contract, Procurement, Permit, MOC, Document IDC) have their own approval entities | #85 (classify each module "workflow-hybrid" or "moves to runtime") |
| 7+ import batch/row entity pairs | Already covered by #25, #217, #360 |
| Dashboards, searches and notifications built several times | Covered by the component work (#323, #326, #328 waves); no separate row |

## 3. Owner decisions (2026-10-29), applied

* **Priority conflicts:** the dependencies were raised to the level of their dependants: #89, #426 to P1; #311, #38, #326, #436, #448 to P0. A scan finds no open row that depends on a lower-priority open row.
* **On Hold dependencies:** #139 (own main host) and #291 (reverse proxy and isolation) are Obsolete, replaced by the shared Plesk/IIS host (#351 done). The dependency was removed from #141, #146, #147, #149, #150, #292, #294, #295, #296, #297, #298, #301. #347 no longer depends on #305; #305 stays On Hold. No open row depends on a Hold row any more.
* **Orphans connected:** #135 -> #145 (demo licence); #443 -> #433, #326; #444 -> #325, #328; #462 -> #85, #445.
