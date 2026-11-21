# Warehouse QA Baseline Results

Database: `PEMSDB_QA407`. Collected on 2026-11-18 at 13:27 UTC using the [read-only baseline script](WAREHOUSE_QA_BASELINE.sql). The database name guard passed. The audit executed aggregate SELECT queries and changed no database records.

| Measure | Result |
| --- | ---: |
| Stock rows | 1 |
| Inventory transactions | 1 |
| Reservation rows | 0 |
| Duplicate stock keys | 0 |
| Invalid active stock quantities | 0 |
| Stock location missing or assigned to another warehouse | 0 |
| Parent location missing or assigned to another warehouse | 0 |
| Invalid active reservation quantities | 0 |
| Open reservations without warehouse attribution | 0 |
| Item/warehouse reservation differences | 0 |
| Receipt movements with no explicit unit of measure | 1 of 1 |

The ledger contains one Receipt dated 2026-10-30 at 13:46:16 (stored timestamp). Verify whether this receipt is intended to inherit the material's base unit and whether the source quantity was recorded in that unit. A missing explicit unit alone does not establish a quantity discrepancy.

Coverage is limited: no reservation, issue, transfer, or adjustment was present in the audited dataset. Zero exceptions do not validate these paths. On-hand reconciliation still requires a verified opening balance and quantity-unit rules. A representative QA snapshot is needed to assess those controls.
