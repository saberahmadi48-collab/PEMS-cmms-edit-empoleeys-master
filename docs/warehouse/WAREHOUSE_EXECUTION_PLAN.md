# Warehouse Execution Plan

## Objective and starting evidence

Protect the integrity of receipts, stock, reservations, and work-order demand. The read-only QA407 baseline contained one stock row, one receipt without an explicit UOM, and no reservations. This establishes the starting coverage and leaves the high-volume and multi-step paths unverified. See [baseline results](WAREHOUSE_QA_BASELINE_RESULTS.md), [code findings](WAREHOUSE_CODE_AUDIT.md), and [product benchmark](WAREHOUSE_PRODUCT_BENCHMARK.md).

## Jira coverage

| Existing issue | Disposition |
| --- | --- |
| [SCRUM-470](https://atapems.atlassian.net/browse/SCRUM-470), [SCRUM-473](https://atapems.atlassian.net/browse/SCRUM-473) | Done. Reuse PO approval and line-level receiving, over/short receiving rules, and OS&D. |
| [SCRUM-471](https://atapems.atlassian.net/browse/SCRUM-471), [SCRUM-472](https://atapems.atlassian.net/browse/SCRUM-472) | Done. Reuse the intake queues and their source references. The new issue covers residual demand and transaction consistency. |
| [SCRUM-428](https://atapems.atlassian.net/browse/SCRUM-428), [SCRUM-478](https://atapems.atlassian.net/browse/SCRUM-478) | Done. Reuse catalogue integration and existing reports. |
| [SCRUM-554](https://atapems.atlassian.net/browse/SCRUM-554) | In Review at planning time. Preserve existing validation for inactive or missing work-order materials. |
| [SCRUM-345](https://atapems.atlassian.net/browse/SCRUM-345), [SCRUM-111](https://atapems.atlassian.net/browse/SCRUM-111), [SCRUM-252](https://atapems.atlassian.net/browse/SCRUM-252) | To Do at planning time. Coordinate UI migration and financial integration with existing tracks. |

## Delivery sequence

| Order | Delivery issue | Proposed owners and acceptance evidence |
| --- | --- | --- |
| 1 | [SCRUM-587](https://atapems.atlassian.net/browse/SCRUM-587), representative baseline and receipt UOM | DBA and Warehouse QA. Approve representative QA data and a trusted opening balance. Capture deployed constraints, quantity and UOM rules, and reconciled ledger, stock, and reservations by item and warehouse. Include receipt, transfer, adjustment, reservation, partial issue, and cancellation. Determine whether the QA407 receipt correctly inherits base UOM. |
| 2 | [SCRUM-588](https://atapems.atlassian.net/browse/SCRUM-588), sourcing and residual demand | PEMS Inventory and CMMS. Address WH-01, WH-04, WH-05 and WH-06. Test independent concurrent sessions and repeated submissions, enforce quantity bounds, and verify residual demand and results of both sourcing branches. Record reconciliation exceptions. |
| 3 | [SCRUM-586](https://atapems.atlassian.net/browse/SCRUM-586), receipt controls and traceability | Warehouse, Quality and PEMS. Address WH-02, WH-03 and WH-07. Approve a warehouse and role matrix and exception receipt policy. Verify quality release, one warehouse count and variance approval, Persian and English messages, and bidirectional traceability for a lot and a serialized item. Policy work can run alongside the baseline; operational sign-off depends on representative QA evidence. |

## Release and sign-off

Use the approved QA dataset for automated relational checks and browser walkthroughs. For each issue, record the test data revision, expected and actual results, and the reviewer. Resolve unexplained quantity differences and verify rollback or retry behavior for inventory postings before rollout. Keep warehouse-wide mobile picking, directed putaway and broader valuation work outside this delivery until the pilot evidence justifies a separate scope.
