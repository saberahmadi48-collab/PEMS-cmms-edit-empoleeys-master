# PEMS Warehouse Relationships and Data Audit

Source baseline: `main` at `a8dd5b6c`. This is a read-only review of EF mappings, service logic, and user-facing flows. An approved database snapshot was not inspected. Actual discrepancy counts, deployed constraints, and historical data quality remain unverified.

## Relationship map and data controls

| Area | Relationship and keys | Data checks |
| --- | --- | --- |
| Warehouses and locations | `StorageLocation.WarehouseId` and optional parent location. Location codes are unique within a warehouse. | Require the parent to belong to the same warehouse. Check for cycles and enforce policy on operational use of inactive warehouses or locations. |
| Material master | `MaterialItem` has classification and a base unit. The same item is referenced by receipts, requisitions, reservations, and the ledger. | Check blank and duplicate normalized codes, valid units and categories, aliases for legacy codes, and manufacturer part-number normalization with the original value retained. |
| Stock balance | `MaterialStock` references an item, warehouse, and optional location. A separate unique index covers rows without a location in the model. | Check duplicates with and without a location, location ownership, negative quantities, reservations exceeding stock, and whether both indexes exist in the deployed database. |
| Receiving and quality control | IRN headers reference vendor, warehouse, purchase order or MRS import batch where applicable, and receiver. Lines reference item, unit, location, and purchase-order line. Disposition records link to the IRN, actor, and NCR. | Verify purchase-order matching and units, order quantity limits, accepted/rejected/held quantities by state, posting only approved accepted stock, and exactly-once posting. |
| Work-order materials and sourcing | `WorkOrderMaterial` references work order, item and unit. The intake workflow uses the work-order ID. Stock lines link to requisition lines. Direct purchases become procurement demand via `SourceType/SourceId`. | Check concurrent sourcing, matching item and work order on linked lines, orphaned demand, and remaining quantities after rejection, cancellation, or short issue. |
| Requisitions and reservations | The requisition references warehouse, optional work order, requester and approver. Its lines reference items and units. Reservations reference item, warehouse, requisition/line, work order or maintenance plan, and actor where applicable. | Reconcile the item, warehouse and source across records. Validate live reservations, segregation of request/approval/issue, partial issue, and release on cancellation. |
| Ledger and stock balance | The ledger references item, warehouse, location, unit, work order, employee, requisition line, reservation, and optional production batch. Ledger and stock balances are separate models. | Reconcile movements from a trusted opening balance. Verify both sides of transfers after UOM conversion and retain the reference and reason for adjustments. |
| Part catalogue and barcodes | Requests link to class, commodity groups, manufacturer, supplier, units and equipment. Procurement can reference a catalogue request. | Verify mapping from assigned part number to warehouse material code, barcode uniqueness in the relevant scope, and missing or multiple mappings. |
| Net demand and replenishment | Recommendations use demand and stock. Procurement demand references item, unit, optional warehouse, and material requisition. | Check double counting, distinguish warehouse-unattributed demand, reproduce calculations from dated inputs, and track stale recommendations. |

Code evidence: `WarehouseStockConfigurations`, `MaterialMasterConfigurations`, `IrnReceiptConfigurations`, `InventoryReservationRequisitionConfigurations`, `InventoryTransactionConfiguration`, `PartCatalogueConfigurations`, and `ProcurementConfigurations`. Runtime paths were traced through `IrnReceiptService`, `StoreRequisitionLifecycleService`, `WarehouseIntakeService`, `PreProcurementService`, `InventoryTransactionService`, and `ReplenishmentRecommendationService`.

## Comparison with international standards

| Reference | PEMS evidence | Gap or evidence needed |
| --- | --- | --- |
| GS1 Global Traceability and EPCIS | Receipt, disposition, issue, and transfer are potential tracking events. Ledger records link to items, locations, actors, and some source references. | Check that critical events have a lot- or instance-level identifier, location, timestamp with time zone, disposition, responsible party, and source document. An optional production-batch link alone does not establish end-to-end lot or serial traceability. |
| ISO 55001:2024 | Links between parts, equipment, work orders, and maintenance plans support asset decisions. | Define data ownership, critical-spares criteria, service-level targets, shortage metrics, and downtime risk with Maintenance and Warehouse. Organizational process evidence is needed to assess alignment. |
| IAS 2, where applicable | The reviewed model supports stock quantity and movement reporting. | Finance must establish the valuation source, cost basis, cost formula, and financial reconciliation. Quantity records alone do not establish valuation compliance. |

Official references: [GS1 Global Traceability Standard](https://ref.gs1.org/standards/global-traceability/), [GS1 EPCIS](https://www.gs1.org/standards/epcis), [ISO/TC 251 on ISO 55001](https://committee.iso.org/sites/tc251/home/projects/published/iso-55001.html), and [IFRS Foundation on IAS 2](https://www.ifrs.org/issued-standards/list-of-standards/ias-2-inventories/).

## Action plan and acceptance criteria

| Order | Proposed owners and work | Acceptance criteria |
| --- | --- | --- |
| 1. Baseline | DBA and Warehouse owner. Use an approved read-only QA database or export to check deployed constraints, orphaned keys, duplicate codes, location/warehouse mismatches, and inactive records used in operations. | Record database revision, extraction date, population, discrepancy count, and anonymized examples for each check. |
| 2. Quantity reconciliation | Warehouse and PEMS team. Compare ledger, stock balance, and open reservations by item, warehouse, location, and base unit. Reconcile opening balances, manual adjustments, transfers, and partial issues separately. | Investigate and disposition every discrepancy. Cover transfers, repeat submissions, and concurrent transactions with tests. |
| 3. Lifecycle controls | Warehouse, QA, and Maintenance. Trace purchase order to IRN and work order to requisition or direct purchase. Define policies for short issue, cancellation, warehouse scope, and segregation of duties. | Unapproved receipts do not create usable stock. For partial sourcing and cancellation, the source and outstanding quantity remain clear. |
| 4. Traceability pilot | Part Catalogue owner and QA. Follow one serialized item and one batch-managed item from receipt through transfer and issue. Measure event-data coverage. | Demonstrate forward and backward traceability between the item and source documents with recorded evidence. Prioritize high-risk items under warehouse policy. |
| 5. Operational controls | Finance, Warehouse, and Maintenance. Define cycle counting, financial reconciliation where applicable, shortage reporting, and replenishment monitoring. Determine whether the sidebar needs an intake-queue shortcut. | Give each control an owner, frequency, exception threshold, and correction approval process. |

Priority controls for the data baseline are location integrity, exclusion of quarantined stock from availability, unique receipt and transfer posting, reservation reconciliation, and traceability of critical items. Decide on data corrections or application changes separately after reviewing the baseline.
