# 0 - trash

Retired files kept for history only. Nothing here is read by the product, the tools or CI; do not edit them.

| File | Why it is here |
| --- | --- |
| `PEMS_Backlog_Roadmap.xlsx` | The plan workbook (version 1.2) until 2026-10-28 (SCRUM-446). The plan is now `plan/rows/NNNN.md` (one file per backlog row) and `plan/sheets/*.md` (the other sheets). The Summary sheet and the Roadmap task counts were formulas; `python tools/plan-sync/plan_sync.py summary` computes them. Its last rows were converted without loss (every cell compared by script). |
