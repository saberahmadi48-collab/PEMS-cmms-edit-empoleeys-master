# Change manifest: retrofit audit of the Sprint 2 work (SCRUM-585, 2026-11-20)

Rule (guideline 04 section 7, PR template): every change declares `migration:`, `feed:`, `definitions:` (or none). This table says what the finished Sprint 2 work needs when a customer updates; counts are the files under `PEMS.Persistence/Definitions/System` today (action 109, component 9, data-source 35, form 28, layout 5, page 44, route 8).

| Work | migration | feed | definitions | How it reaches a customer |
|---|---|---|---|---|
| Definition store (SCRUM-434) | `20261028111608_DefinitionStore` | none | none | applied by the database update at start |
| Shipped definitions and upgrade rule (448, 515) | none | none | every file of the table above | `DefinitionService.ShipAsync` at start: new ones are published, customised ones keep their version and get an upstream draft; platform fields of forms are merged |
| Entity pages, forms, actions (departments, roles, uoms, material categories, warehouses, maintenance groups, skills, qualifications, storage locations, spare parts, audit views, SoD) | none | none | page, form, action, data-source, route files | as above |
| Operation actions of Work Request, Permit, Work Order, MOC, Isolation, Contract, Production Order, Tender, Technical Query, Meeting (SCRUM-437 and follow-ups) | none | none | 80+ action files (`operation.invoke`) | as above; the operations are code in the release |
| Component definitions and templates (589 and batch 1) | none | none | 9 component files, template pages | as above |
| Definition grants (591) | `20261119190301_Scrum591DefinitionGrants` | none | none | database update; the grants are rows the customer's administrator edits |
| Default grants of IRN and VDR holds (547, 559) | `20261109050753_Scrum547IrnDefaultGrants`, `20261110051551_Scrum559VdrHoldGrants` | grants rows inside the migration | none | database update |
| Menu definitions (590) | none | menu items synced at start with the ownership rule | `menu` definitions when shipped | sync at start (what an administrator changed is never touched) |
| Shipped workflows (517) and reference data (516) | none | workflow documents, reference-data lists | none | workflow shipper and `ReferenceDataSync` at start |
| Definition workbench (449, 450, 455, 419, 609, 454) | none | none | 7 data-source, form, action and 5 page files named `definition-*` | as above |
| Packages run and saved views (SCRUM-807, 814, 793) | `20261121010000_Scrum807PackagesAndViews` (tables AppliedPackages and GridSavedViews) | none | data-source `grid-views`, actions `grid-view-save`, `grid-view-delete`, history, import and preview pages | database update creates the tables; the views are rows users write |

Not covered yet (a gap, owner of the next issue: SCRUM-581 and 582): the update **package** that carries these to a customer without the product starting on its own. Today they travel with the release and run at start (auto-run stays, as the issue says).

Check in the test gates: `DefinitionCutoverTests` and `PilotPagesTests` already fail when a shipped definition is not valid or references something missing, which is the "every shipped definition is covered" check; `ChangeManifestCoverageTests` (SCRUM-791, namespace Definitions, picked by gates.py) fails for a migration dated after 2026-11-20 whose id is not named in this file and for a shipped definition kind folder this file does not name: add the row here in the same PR as the migration.
