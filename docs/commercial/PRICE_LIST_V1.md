# Price list v1 (STRUCTURE with placeholders, for owner numbers)

Plan row #130 (SCRUM-28). **Status: structure only. No price in this file is a decision.** Every number is a placeholder written as `{{NAME}}` for the owner to fill; nothing was invented. Prepared 2026-11-03 for Hamid Maleki. Editions and modules come from [MODULE_CATALOGUE_DRAFT.md](MODULE_CATALOGUE_DRAFT.md) (row #112) and must match it word for word once approved.

Approved by: _not yet_

## 1. The model (decided by the owner, 2026-10-18)

- **Perpetual licence** per edition and per module, paid once.
- **Annual maintenance** (percentage of the licence price) controls which updates a customer may install (`updates_until` in the licence, CV_UPDENT). Without maintenance the installed version keeps working but receives no new versions.
- **No user limit.** Limits are on sites and modules only (Supportability Matrix).
- Two price lists: **domestic (IRR, shown in rial; toman is a display scale only)** and **international (currency `{{INTL_CURRENCY}}`)**.

## 2. Core edition licence

| Edition | Domestic price (IRR) | International price | Included modules |
|---|---|---|---|
| Foundation | `{{PRICE_FOUNDATION_IRR}}` | `{{PRICE_FOUNDATION_INTL}}` | core + one module of the customer's choice |
| Professional | `{{PRICE_PROFESSIONAL_IRR}}` | `{{PRICE_PROFESSIONAL_INTL}}` | core + maintenance suite |
| Enterprise | `{{PRICE_ENTERPRISE_IRR}}` | `{{PRICE_ENTERPRISE_INTL}}` | core + all modules |

## 3. Module licences (per module, perpetual)

| Module key | Module | Domestic price (IRR) | International price |
|---|---|---|---|
| `mod.assets` | Assets and equipment | `{{PRICE_ASSETS_IRR}}` | `{{PRICE_ASSETS_INTL}}` |
| `mod.maintenance` | Maintenance (CMMS) | `{{PRICE_MAINTENANCE_IRR}}` | `{{PRICE_MAINTENANCE_INTL}}` |
| `mod.reliability` | Reliability | `{{PRICE_RELIABILITY_IRR}}` | `{{PRICE_RELIABILITY_INTL}}` |
| `mod.permits` | Permit to work and safety | `{{PRICE_PERMITS_IRR}}` | `{{PRICE_PERMITS_INTL}}` |
| `mod.inventory` | Warehouse and inventory | `{{PRICE_INVENTORY_IRR}}` | `{{PRICE_INVENTORY_INTL}}` |
| `mod.procurement` | Procurement | `{{PRICE_PROCUREMENT_IRR}}` | `{{PRICE_PROCUREMENT_INTL}}` |
| `mod.documents` | Engineering documents | `{{PRICE_DOCUMENTS_IRR}}` | `{{PRICE_DOCUMENTS_INTL}}` |
| `mod.quality` | Quality and change | `{{PRICE_QUALITY_IRR}}` | `{{PRICE_QUALITY_INTL}}` |
| `mod.correspondence` | Correspondence | `{{PRICE_CORRESPONDENCE_IRR}}` | `{{PRICE_CORRESPONDENCE_INTL}}` |
| `mod.production` | Production | `{{PRICE_PRODUCTION_IRR}}` | `{{PRICE_PRODUCTION_INTL}}` |
| `mod.ai` | AI assistance | `{{PRICE_AI_IRR}}` | `{{PRICE_AI_INTL}}` |
| `mod.projects` | Projects | `{{PRICE_PROJECTS_IRR}}` | `{{PRICE_PROJECTS_INTL}}` |

## 4. Annual maintenance

| Item | Value |
|---|---|
| Maintenance rate (percent of the licence price, per year) | `{{MAINTENANCE_PERCENT}}` |
| First maintenance year | `{{MAINTENANCE_FIRST_YEAR}}` (included in the licence price, or charged from year 1) |
| Renewal window and late renewal rule | `{{MAINTENANCE_RENEWAL_RULE}}` |
| What maintenance covers | new versions up to `updates_until`, support terms of the proposal template |
| Reinstating lapsed maintenance | `{{MAINTENANCE_REINSTATE_RULE}}` |

## 5. Discount rules

| Rule | Value |
|---|---|
| Multiple modules in one order | `{{DISCOUNT_BUNDLE}}` |
| Additional site | `{{PRICE_EXTRA_SITE}}` (limits are on sites and modules, not users) |
| Government or group customers | `{{DISCOUNT_SPECIAL}}` |
| Maximum discount without owner approval | `{{DISCOUNT_MAX}}` |

## 6. Assumptions to confirm (written down as the task asks)

1. Domestic prices are quoted in rial and invoiced per Iranian rules; the international list is a separate price list, not a conversion.
2. Installation, training, data migration and customisation are services outside this list: `{{SERVICE_RATES}}`.
3. The price list names exactly the editions and modules of the approved catalogue; a module added to the catalogue is not for sale until it has a row here.
4. Prices are valid until `{{PRICE_VALIDITY}}` and do not include taxes: `{{TAX_RULE}}`.

## 7. Owner checklist

- [ ] Fill every `{{...}}` (about 45 values).
- [ ] Approve the module names against the approved catalogue (#112).
- [ ] Approve the template ([PROPOSAL_TEMPLATE.md](PROPOSAL_TEMPLATE.md), Persian and English).
