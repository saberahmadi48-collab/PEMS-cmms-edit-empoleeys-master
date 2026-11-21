# Hard-coded data audit: what should be data, not code

Issue: SCRUM-514 (plan row 514). Date: 2026-11-06. Author: Claude, for Hamid. Status: first audit, for the owner to read and rank.

## 1. What this is and how it was made

The owner asked to separate the data from the code: form lists, form fields and steps, validations, sidebar items, list columns, page elements, workflows, lookups. The definition store already exists (ADR-0010, ADR-0011, SCRUM-434; envelope with versions, Origin System/Customer, ShippedVersionId, IsCustomized). This audit measures how much is still outside it.

**Method and limits.** Counts come from searching the repository (file counts, attribute counts, seeder classes). They show size and direction, not exact figures: a count of `[MaxLength]` says nothing about how many rules are business rules. Nothing was run; no database was read. Where a statement is a judgement it says so.

## 2. The three boxes

| Box | Meaning | Edited by | Lives in |
|---|---|---|---|
| **Settings** | How the application behaves on one machine or customer | the installer / operator | `env/*.env`, secrets in `*.secrets.env` (ADR-0004) |
| **Definitions** | What users see and do: forms, menus, lists, pages, workflows, reference lists | designers (customer, our team) | the definition store; the shipped set is delivered with the product as System definitions |
| **Registry** | The vocabulary the code owns and promises: field types, validator types, actions, permissions, components, layouts | developers only | code; readable by the designers |

Rule of thumb: data says *what*, code says *how*. Code defines the vocabulary; definitions compose with it.

## 3. Inventory

| # | Area | Where it is today | Size (measured) | Box | Target | Covered by | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | Sidebar and menu | `NavigationMenuDefaults.cs` (code catalog, 119 entries) synced into the table `NavigationMenuItems`; Admin edits order, parent, icon, titles, visibility | 119 entries, 209 lines | Definitions | kind `menu`, shipped as System definitions | #20 (generated from the user's authority), #451, #448 | Owner 2026-11-06: generate from what each user may open; admin-owned fields kept |
| 2 | Forms (screens and their fields) | hand-written Razor: 240 view files contain a `<form>`; 365 `.cshtml` in total | 240 files | Definitions | kind `form` (exists, stored forms are used only by the definitions API today, not by modules) | #518 (adoption, all forms, safely), #438, #439, #49, #458 | Biggest gap; all forms in large batches, replaced in place after a parity test, no switch |
| 3 | Form steps and field layout | inside those views | same | Definitions | kind `form` (steps) and `layout` | #518, #451 (layout) | Same as 2 |
| 4 | List pages: columns, filters | 202 views use `pems-list-*` tag helpers; columns are written in the markup | 202 files | Definitions | kind `view` | #519 (adoption, all lists, safely), #451 (view), #57 | Large gap; value high |
| 5 | Validations | DataAnnotations: 31 `[Required]`, 50 `[MaxLength]`, 2 `[EmailAddress]` (26 files); 47 `ModelState.AddModelError`; 7 thrown validation-style exceptions in Application/Infrastructure | about 140 spots | Registry (validator types) plus Definitions (which rule on which field) | form field constraints in the `form` kind; named code validators for complex rules | #355, #356, #365, #380, #518 | Small today; moves with the forms in #518 |
| 6 | Workflows | engine and tables exist (`PEMS.WorkflowPlatform`, versioned definitions, Admin > Workflow Management); the **content** of about 20 workflows is created by seeder classes in C# | 20 `*Workflow*Seeder` classes | Definitions | shipped workflow definitions | #517 (shipped workflow content), #448 | Engine done; shipped-content delivery and the "customised workflow survives an update" rule are open |
| 7 | Reference lists and lookups | 61 seeder classes, 8,163 lines in `PEMS.Persistence/Seed` (departments, disciplines, failure codes, UOM, categories, strategy types, numbering schemes, letter templates, help content, roles) | 61 classes | Definitions (reference data) | a `lookup` kind or shipped rows with the same "customer data wins" rule | #516 (new) | Owner 2026-11-06: either side may own a list or row, so the rule supports both |
| 8 | Page elements and layouts | 111 components in `PEMS.UI`; manifests exist (#433) | 111 components | Registry | stays in code; pages built from them are Definitions | #433 done, #438, #439 | Registry side is done; page host open |
| 9 | Permissions | catalog in code (`PEMS.Web/Security`, `RoleCatalogSeeder`, `AccessControlSeeder`); roles and grants are data | 12 constants plus seeders | Registry (permission names) plus Definitions (roles, grants) | permission names stay code; per-kind definition permissions | #456, #440 | Mostly right |
| 10 | Command palette actions | `CommandPaletteActions.cs` | 36 lines | Registry | code, or derived from the menu definitions | #20 | Generated from the same source as the menu |
| 11 | Texts | `SharedResource.resx` 3,690 keys (fa 3,980); no Persian literal found in `.cshtml` | 3,690 keys | Definitions (texts of stored definitions are `TranslatableText`, ADR-0008) | unchanged | #386, #387 | Fine |
| 12 | Settings and paths | 83 non-comment lines in `env/local.env`; hard-coded: file store `filestore` and `wwwroot` fallbacks in `Program.cs`, break-glass folder | few | Settings | env files | #396 | Fine; one open issue |
| 13 | Numbering, letter and PM templates | `NumberingScheme`, `LetterTemplate`, `PMTemplate` entities, seeded in code | seeders | Definitions (reference data) | same as 7 | #516 | Same as 7 |

## 4. What already works and should be copied

* **The sidebar already has the platform/application split.** The code catalog owns route, visibility rule, kind and active-state hints; the admin owns order, parent, icon, titles, hidden flag and badge; the sync adds missing items and updates only code-owned fields, never admin-owned ones (`docs/architecture/DYNAMIC_SIDEBAR.md`). That is exactly the rule of SCRUM-515 (platform fields ours, application fields the customer's). SCRUM-515 should generalise this pattern, not invent a new one.
* **The definition store** already has Origin, ShippedVersionId, IsCustomized, versions with Draft/Validated/Published/Retired, and a cache contract (METADATA_REGISTRY_DESIGN sections 3 and 5).
* **Workflows** are already data with an engine, versions and an admin screen.
* **Texts** are already data (resx) and component inputs are `TranslatableText`.

## 5. Ranking (judgement, for the owner to change)

Ordered by value for the "dynamic BPMS" goal divided by effort.

1. **Rule first, before any mass move** (#515 and #448): without the "customer data wins, a conflicting update stops with a report" rule, every other move is unsafe. Small effort, unlocks the rest.
2. **Reference data and workflow content (rows 6, 7, 13)**: 61 seeders and about 20 workflow seeders. Highest risk today, because an update can overwrite what the customer changed. Needs the same rule and a shipped-data format. Medium effort.
3. **Menu onto the definition store (row 1)**: smallest, already almost there; a good first proof of #448 and #451.
4. **Forms and steps (rows 2, 3, 5)**: the biggest gain and the biggest effort (240 files). Do one pilot form end to end (fields, steps, validation), then module by module.
5. **List pages (row 4)**: 202 files, large but mechanical once the `view` kind exists.
6. Leave alone: Registry items (rows 8, 9, 10), texts (11), settings (12).

## 6. Issues created or changed from this audit (owner answers 2026-11-06)

Created: #515 field ownership (platform / application fields, update rule), #516 reference-data ownership and update rule (lookups, templates, numbering; either side may own a list or row), #517 shipped workflow content, #518 existing forms become stored form definitions (all modules, safely, no pilot), #519 existing list pages become stored view definitions (all modules, safely, no pilot).

Changed instead of created (rule 28): #448 (basic whole-definition rule, refined by #515; menu defaults become shipped), #440 (actor x target policy table), #20 (the sidebar is generated from the definitions each user may open; the command palette uses the same source).

Left as they are: Registry items (rows 8, 9, 10), texts (11), settings (12, covered by #396).

## 7. Owner answers (2026-11-06)

1. Reference data: "maybe both, I don't know": ownership is set per list and per row (#516), with a default per list for the owner to confirm.
2. Pilot form: none; each task is done for all forms and lists, but safely (each form or list replaced in place with its old view deleted, a parity test first, no switch and no fallback, large batches; #518, #519).
3. Sidebar: generated from the authority of each user over the forms, lists and pages (#20), not a hand-kept table.
