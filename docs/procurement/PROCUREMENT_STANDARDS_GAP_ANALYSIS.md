# Procurement standards gap analysis (SCRUM-427)

Status: 2026-10-27. Analysis only; no code changes. The evidence is the repository on `main` at 93ed640, plus the earlier audits:
- `docs/audits/PEMS_SUPPLY_CHAIN_PROCUREMENT_BASELINE_AUDIT.md` (2026-10-08);
- `docs/procurement/P0_HARDENING_GATE_EVIDENCE.md` (2026-10-12);
- the target process in `docs/PEMS_Procurement_Lifecycle_Spec/`.

Where this document says "verified", the code was read for this ticket. Every other claim is quoted from those audits and marked as such.

## Persian summary (خلاصهٔ فارسی)

PEMS بیشتر حلقه‌های اصلی چرخهٔ تدارکات را دارد:
- نیاز داخلی انبار (Indent، در سامانه Store Requisition)؛
- MR؛
- بستهٔ خرید؛
- مناقصه با دعوت فروشنده، پیشنهاد، ارزیابی و مذاکره؛
- قرارداد با گردش‌کار، بررسی رشته‌ها و حدود اختیار مبلغی؛
- VDR؛
- رسید IRN با تعیین تکلیف؛
- ITP و بازرسی؛
- صلاحیت فروشنده؛
- نیاز خالص و پیشنهاد تأمین.

شکاف‌های اصلی نسبت به رویهٔ جهانی (پروژه‌های EPC نفت و گاز، چرخهٔ CIPS، مدیریت تدارکات PMI و ISO 20400) این‌هاست:
1. **سفارش خرید (PO) فقط سربرگ است:** ردیف کالا و پیوند به MR، مناقصه یا برنده ندارد. PO دستی از «پیش‌نویس» شروع می‌شود و با یک مرحلهٔ جدا صادر می‌شود، ولی گردش تأیید و حدود اختیار مبلغی ندارد. PO وارداتی مستقیم «صادرشده» ساخته می‌شود. بنابراین ردیابی «ردیف MR ← پیشنهاد ← ردیف PO ← ردیف رسید» ممکن نیست.
2. **ثبت رسمی برنده (Award) و تأیید آن وجود ندارد:** پیشنهاد فقط مبلغ کل دارد و قیمت ردیفی ندارد. این‌ها هم نیست:
   - قفل پیشنهادها تا زمان بازگشایی؛
   - ارسال سؤال و پاسخ به همهٔ شرکت‌کنندگان؛
   - حداقل سه فروشنده یا ثبت علت؛
   - تفکیک وظایف ارزیاب و تأییدکننده.
3. **ارزیابی فنی و بازرگانی (TBE/CBE)** ماتریس معیار وزنی ندارد.
4. **از کمبود Indent به درخواست خرید** پیوندی نیست. پیشنهاد تأمین هم به MR تبدیل نمی‌شود.
5. **پیگیری تحویل (Expediting)**، آزادسازی بازرسی پیش از ارسال، گزارش مغایرت رسید (OS&D) و تطبیق سه‌طرفه با فاکتور ناقص است یا وجود ندارد.
6. **شماره‌گذاری اسناد تدارکات دستی است** (SCRUM-40). قلم‌های MR به کاتالوگ قطعه وصل نیستند (SCRUM-428). ارسال RFQ با ایمیل وجود ندارد (SCRUM-429).

پیشنهاد ترتیب کار در بخش ۶ آمده است. ابتدا همان چهار تیکت تأییدشده، بعد PO با ردیف و تأیید، بعد Award و ارزیابی.

## 1. Reference model

This model uses public, widely used frameworks. Only their structure is used here; none of their text is copied.

| Framework | What it contributes here |
|---|---|
| CIPS procurement and supply cycle | The end-to-end stages: define need, specification, sourcing, tender, evaluation, award, contract, ordering, expediting, receipt, payment, supplier and contract review. |
| PMI project procurement management (PMBOK) | Plan → conduct → control procurements: make-or-buy, bidder conferences, source-selection criteria, change control, claims, close-out. |
| Typical oil & gas / EPC practice (owner-operator and contractor) | MR → RFQ to a qualified bidders list → technical bid evaluation (TBE) by engineering, commercial bid evaluation (CBE) by procurement → recommendation → PO. After the PO: vendor document register (VDR), expediting, ITP / pre-inspection meeting / FAT, inspection release note, shipping, receiving (MRR/IRN), OS&D, close-out and vendor performance. |
| ISO 20400 (sustainable procurement guidance) | Sustainability and ethics criteria in the specification and evaluation, supplier code of conduct, and risk-based supplier engagement. |
| Public procurement principles (UNCITRAL model law, World Bank style) | Transparency, competition (minimum number of bidders, documented exceptions), sealed bids until opening, equal information to all bidders, documented award decision. |
| Internal control (COSO style) | Segregation of duties (requester, buyer, evaluator, approver, receiver, payer), delegation of authority by amount, approval before commitment, audit trail, three-way match. |

## 2. What PEMS has today (inventory)

| Area | PEMS today | Evidence |
|---|---|---|
| Indent (internal demand) | `StoreRequisition` with lines, reservation, partial issue, SoD (requester ≠ approver/issuer), guarded lifecycle, concurrency tests | `PEMS.Domain/Entities/StoreRequisition*.cs`, `StoreRequisitionLifecycleService.cs`; baseline audit section 12 |
| Net demand and replenishment | net-demand runs and replenishment recommendations (behind a feature flag) | `ReplenishmentRecommendation.cs`, `docs/procurement/P5_*` |
| Part catalogue | catalogue requests, duplicate search, approval workflow, registered part number (`AssignedPartNumber`), Material Master (`MaterialCode`) | `PEMS.Domain/Entities/PartCatalogue/`, `PartCatalogueService.cs` |
| Material requisition (MR) | MR per project and discipline with items, a guarded status table (Draft → Submitted …), link to package | `MaterialRequisition*.cs`, `MaterialRequisitionService.cs` (verified) |
| Vendor quote (MRQ) | quotation per MR/package with free-text vendor name and evaluation strings | `MaterialRequisitionQuotation.cs` (verified) |
| Procurement package | scope container: stage, criticality, inspection flag, communications, approvals | `ProcurementPackage.cs`; baseline audit P1 |
| Tender | tender with invited vendors (qualification check), bids (original, negotiated and final value), evaluations, negotiation rounds, award fields, contract from the winning bid | `Tender.cs`, `Bid*.cs`, `TenderService.cs` (verified fields) |
| Vendor master and qualification | qualification status, category, validity dates, evidence, history; one contact email | `Vendor.cs`, `VendorQualificationHistory.cs` (verified `ContactEmail`) |
| Contract | contract with revisions, discipline reviews, NTP, documents, workflow on the engine, approval authority rules by value and criticality | `Contract*.cs`, `ContractApprovalAuthorityRule.cs`, `PemsWorkflowContracts.cs` (verified) |
| Purchase order | header only: number, project, vendor, optional contract, value, currency. A manual PO starts as `Draft` and `IssueAsync` moves it to `Issued`; the import path creates `Issued` directly | `PurchaseOrder.cs`, `PurchaseOrderService.cs` (verified) |
| Vendor documents | VDR per PO/MR with review workflow, commercial and engineering expeditor names | `VDRItem.cs` (verified expeditor fields) |
| Inspection | ITP, ITP line items, inspection checklist and records (QC module); inspection gate on the package | `InspectionTestPlan.cs`, `ITPLineItem.cs`, `ProcurementPackageInspectionGateTests` |
| Receiving | IRN with lines (per material item), status history, disposition record with an approval workflow, MRS import into stock | `IrnReceipt*.cs`, `IrnDispositionRecord.cs`, `MrsImport*.cs` |
| Numbering | central atomic counter (`NumberingService`); prefixes are hard-coded per document type; package, tender, PO and contract numbers are typed by hand | `NumberingService.cs` (verified) |
| Email | none | no SMTP or mail library in the solution (verified) |
| Selects | Tom Select is vendored and used for user roles only | `PEMS.Web/wwwroot/lib/tom-select` |
| Navigation | one "Procurement & Supply Chain" section (SCRUM-423) | `NavigationMenuDefaults.cs` |

## 3. Lifecycle gap matrix

Status: ✅ present · 🟡 partial · ❌ missing.

| # | Stage / control (reference) | PEMS | Gap |
|---|---|---|---|
| 1 | Need identification: indent, MR, maintenance demand | ✅ | Store Requisition, MR and work-order demand all exist. |
| 2 | Indent shortage → purchase requisition | ❌ | No link from a short Store Requisition line to an MR or purchase requisition (baseline audit section 7). Replenishment recommendations cannot be converted to an MR. |
| 3 | Item identification from a catalogue | 🟡 | The part catalogue exists, but MR items are free text and not linked to Material Master (SCRUM-428). |
| 4 | Specification and technical requirements | ✅ | Tender technical, commercial, inspection and contractual requirement fields exist; MRs link engineering documents. |
| 5 | Make-or-buy / stock-or-buy decision | 🟡 | The net-demand calculation exists; there is no recorded decision on the requisition. |
| 6 | Budget check and commitment | ❌ | No budget or cost code on MR or PO. Finance is planned in SCRUM-252 (M6). |
| 7 | Bidders list from qualified vendors | 🟡 | Invited vendors carry a qualification check. There is no rule for a minimum number of bidders with a recorded reason when fewer are invited. |
| 8 | RFQ issue and receipt log | ❌ | No sending channel and no "who received what, when". RFQ email is SCRUM-429; the spec also allows a recorded manual channel. |
| 9 | Bid clarifications (Q&A to all bidders) | ❌ | No clarification register and no broadcast of answers to every invited vendor. |
| 10 | Sealed bids until opening | ❌ | `TenderService` accepts a bid whenever the tender is `Open`. `ResponseDeadline` is stored but not enforced, and bid values are not hidden until a formal opening event with attendees (verified). |
| 11 | Technical bid evaluation (TBE) | 🟡 | `BidEvaluation` records by type and employee exist; there is no weighted criteria matrix, pass/fail threshold or report. |
| 12 | Commercial bid evaluation (CBE) | 🟡 | Bid values and negotiation rounds exist. There is no price per line, no currency normalisation and no evaluated price including terms. |
| 13 | Evaluator independence and conflict-of-interest declaration | ❌ | Technical and commercial evaluators are not forced apart (baseline audit P3). No declaration is recorded. |
| 14 | Award recommendation and approval by authority (DoA) | 🟡 | The award is three fields on the tender. Authority rules exist for contracts only. There is no award record, approval state or split award. |
| 15 | Contract | ✅ | Workflow, discipline reviews, revisions, NTP and authority rules. Milestones and claims are "coming soon". |
| 16 | Purchase order with lines | ❌ | The PO is header only, with no lines and no link to MR, package, tender or award (baseline audit P4, verified). |
| 17 | PO approval before commitment | 🟡 | A manual PO starts as `Draft` and is issued in a separate step (`PurchaseOrderService.IssueAsync`). There is no approval workflow and no authority by amount; imported POs are created as `Issued`. (Corrected 2026-10-27: the first version said new POs start as `Issued`.) |
| 18 | Document numbering under admin control | ❌ | Manual numbers for package, tender, PO and contract (SCRUM-40). |
| 19 | Vendor documents (VDR) and approvals | ✅ | VDR register and review workflow per PO/MR. |
| 20 | Expediting | 🟡 | Only expeditor names on VDR items. No PO delivery schedule, promised dates, expediting log or alert for late items. |
| 21 | Inspection (ITP, PIM, FAT) and inspection release note | 🟡 | ITP and inspection records exist in QC, and there is a package inspection gate. They are not linked to PO lines, and no release note gates shipment. |
| 22 | Shipping and logistics | ❌ | No shipment or packing-list record. |
| 23 | Receiving (MRR/IRN) against PO lines | 🟡 | IRN lines are per material item, not per PO line. Over- and short-receipt control against PO quantities is impossible without PO lines. |
| 24 | OS&D (over, short and damaged) report | 🟡 | IRN disposition with an approval workflow covers rejections; there is no OS&D report to the vendor. |
| 25 | Invoice and three-way match | ❌ | No invoice or AP (SCRUM-252, M6). |
| 26 | Close-out (PO, package, contract) | 🟡 | Package "Closeout" stage exists, but there is no close-out checklist (all received, VDR final, warranty, documents). |
| 27 | Vendor performance evaluation | ❌ | No post-delivery KPIs (on-time delivery, quality, document timeliness) feeding qualification. |
| 28 | Sustainability and ethics criteria (ISO 20400) | ❌ | No sustainability criteria in the evaluation and no supplier code of conduct acknowledgement. Low priority. |
| 29 | Segregation of duties along the chain | 🟡 | Strong for Store Requisition, contracts and part catalogue. Missing for evaluator vs approver and for PO creator vs approver. |
| 30 | Audit trail | ✅ | Audit log, workflow history, status history on Store Requisition and IRN. |
| 31 | Reporting / KPIs (cycle time, savings, tenders) | 🟡 | `ProcurementReportService` is MRQ-based; there are no tender, award or PO KPIs (baseline audit P7). |
| 32 | Usability: searchable selects, lookups | 🟡 | Plain selects on most forms (SCRUM-57). |

## 4. Indent (question 3 of the owner)

PEMS already has the internal indent. It is called **Store Requisition** (درخواست انبار) and lives under Procurement & Supply Chain → Warehouse. No new "Indent" entity is needed. Re-creating it would duplicate a working, tested module.

The standard flow is: indent → stock check → issue from stock, or, when short, a purchase requisition / MR for the net shortage.

PEMS covers the first half. The missing piece is gap #2: a short line should produce or link to an MR line, keeping the origin for traceability.

## 5. Notes on the four approved tickets

- **SCRUM-40 (numbering):** cover package, MR, tender, PO and contract. The admin defines the pattern per document type:
  - a prefix;
  - year, Jalali or Gregorian;
  - optional project and discipline codes;
  - a counter with padding and a reset period.

  Use the existing atomic `NumberingService` counter and a unique index per number. Existing numbers are not renumbered. Manual entry stays possible only with a permission, for numbers imported from outside.
- **SCRUM-57 (searchable selects):** add it once in the component library, for every form:
  - client search for small lists;
  - server-side search for large ones (vendors, part numbers, employees, equipment).

  This is also the lookup that SCRUM-428 needs.
- **SCRUM-428 (part numbers):** the MR item stores the Material Master link plus a snapshot. It is the first step to line-level traceability (gaps #3, #16, #23).
- **SCRUM-429 (RFQ email):** this closes gap #8 for the email channel. The send log doubles as the RFQ receipt log. Manual channels (hand delivery, letter) should be recordable in the same log, as the spec requires for sites without email.

## 6. Proposed follow-up tickets, by value

Each needs the owner's approval before a Jira ticket is created.

| Order | Proposal | Closes gaps | Size | Notes |
|---|---|---|---|---|
| 1 | **PO lines and source links, Draft → approval → Issued** through the workflow engine, with authority by amount (reuse `ContractApprovalAuthorityRule` as a general DoA) and SoD (creator ≠ approver). Imports keep creating `Issued`. | 16, 17, 29 | L | Baseline audit decision 5 still open: does the organisation approve POs in PEMS? |
| 2 | **Award record and approval:** `TenderAward` with lines (split award), recommendation, approval by authority; bid prices per line (MR item). | 12, 14 | L | Extends `Tender`/`Bid`; nothing is re-created (baseline audit decision 2). |
| 3 | **Tender integrity:** sealed bids until the opening event (attendees, opening minutes), clarification register with answers sent to every bidder, minimum bidders with a recorded reason, evaluator conflict-of-interest declaration and technical ≠ commercial evaluator. | 7, 9, 10, 13 | M | Uses SCRUM-429 for broadcasting. |
| 4 | **Structured TBE/CBE:** weighted criteria templates, pass/fail technical threshold, evaluated commercial price (currency, terms), printable evaluation report. | 11, 12 | M | |
| 5 | **Indent shortage → MR:** convert a short Store Requisition line or a replenishment recommendation into an MR line, keeping the origin link. | 2, 5 | M | |
| 6 | **Expediting:** PO line delivery schedule, promised and forecast dates, expediting log, late-item alerts on My Work. | 20 | M | Needs item 1. |
| 7 | **Receiving against PO lines and OS&D:** IRN line → PO line, over- and short-receipt control, OS&D report to the vendor; link the inspection release note (ITP) to PO lines before shipment. | 21, 23, 24 | M | Needs item 1. |
| 8 | **Close-out and vendor performance:** close-out checklist for PO, package and contract; vendor KPIs (OTD, quality, document timeliness) feeding qualification. | 26, 27 | M | |
| 9 | **Procurement KPIs dashboard:** cycle time per stage, tenders, awards, savings, late POs. | 31 | S | After items 1–2. |
| 10 | **Shipping and logistics:** shipment, packing list, transport and customs status. | 22 | M | Only if the organisation tracks shipments in PEMS. |
| 11 | **Sustainability criteria and code of conduct** (ISO 20400). | 28 | S | Low. |
| — | Invoice and three-way match | 6, 25 | — | Already planned: SCRUM-252 (M6). Not duplicated. |

## 7. Open policy questions for the owner

These come from the lifecycle spec, section 14, and are still open:
- authority limits by amount;
- whether POs are approved in PEMS;
- the minimum number of bidders;
- whether catalogue approval is required before every purchase;
- the RFQ channel where there is no email.

The proposals above work with the answers left configurable. None of them hard-codes a limit.
