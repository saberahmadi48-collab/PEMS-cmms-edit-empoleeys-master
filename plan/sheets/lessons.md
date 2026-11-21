# Lessons

Project memory - facts and lessons

| - | PEMS is a brand-new project. Its task source of truth is the plan folder (plan/rows, one markdown file per task; until 2026-10-28 an Excel workbook). |
| --- | --- |
| - | Repository: PEMS-cmms-edit-empoleeys-master (public GitHub under a personal account - ownership/licence to be clarified, row B_ENTITY). Stack: .NET 10, EF Core 10, SQL Server, Hangfire, QuestPDF, Fido2, Serilog, Ollama. |
| - | Working language of the product is Persian. The app's default culture is currently en and the calendar is deliberately Gregorian (Program.cs) - rows LOC_DEFAULT and LOC_CAL. |
| - | Licensing: on-prem, machine + install-time lock; request/licence file process; lifetime or term (a time period); product name is PEMS (decided). |
| - | Old audit workbooks are kept in '0 - trash'; the previous Persian edition and the Excel workbook of the plan (until 2026-10-28) are kept there too. |
| - | Not yet done as of 2026-10-15: build/test run, licensing, installer, builders. |

| # | Lesson | Reference | Explanation | PEMS rows |
| --- | --- | --- | --- | --- |
| 1 | Registry-driven, not code-driven | Titan Overview | Apps/models/fields/statuses/licences are database rows and one generic CRUD/export/import engine runs them; adding an app means seeding metadata. | #10 #12 |
| 2 | A registry change must not require a rebuild | Titan #61 | A static export forced a full rebuild and redeploy after each registry change; PEMS is server-rendered but must keep this property. | #28 |
| 3 | Registry cache + ETag + gzip | Titan #50 | Database slowness was fixed by caching the metadata registry and conditional GET; build it in from the start. | #28 |
| 4 | One contract for server-side paging | Titan #49 #136 | It stayed incomplete twice; use one query contract and one grid, not patch by patch. | #12 #32 |
| 5 | Changing a field type on live data is the riskiest operation | Titan #17 | Needs guided ALTER, rollback and a data-risk warning. | #13 |
| 6 | Licence was enforced only in production | Titan #143 #204 | Staging/local booted without a licence, and a LICENSE_BYPASS flag was left behind. | #110 |
| 7 | The first-run licence flow was fragile | Titan #142 #144 | The setup token was not created in one case; the rate limiter was tied to the DB and requests hung. | #109 |
| 8 | Offer the fingerprint in the UI, not the terminal | Titan #141 | Copy/download of the fingerprint from the page; delivery as zip and automatic restart. | #108 #100 |
| 9 | Licence issuing in a separate tool, never in the customer package | Titan #145 | Two flags (build/run); the superowner tool stays with the vendor. | #106 #119 |
| 10 | Public surfaces must not need a licence | Titan #163 #168 | An explicit allowlist; app licensing derived from model licences. | #111 |
| 11 | Signing keys are a permanent operational responsibility | Titan #207 | A key procedure is needed, not just code. | #107 |
| 12 | Package allowlist for releases | Titan #68 #137 | Secrets and development files remained in the package; allowlist scan. | #119 #9 |
| 13 | Signed offline updates | Titan #249 #130 #206 | A signed package, an Update page and manual support operations. | #170 #175 |
| 14 | Installer, updater and migrator are three separate jobs | Titan Instructions 13 | Fresh install strict, update tolerant, the migrator the low-level engine; never edit an applied migration. | #167 #170 |
| 15 | SaaS is in tension with the offline model | Titan #78 | Multi-tenancy was cancelled; solve the demo with an isolated instance. | #138 |
| 16 | Live brand profile from the database | Titan #98 #241 #236 | Logo/name/colors on the login page come from the DB; bugs with a broken logo and the wrong profile. | #129 |
| 17 | A mobile view must really work on every screen | Titan #177 | Responsive alone is not enough; a QA matrix. | #39 #223 |
| 18 | Library-first, even retroactively | Titan Instructions 2 | Bytecode protection, security middleware, password hashing, TOTP, server-side Excel, migrator, logging, virtualized lists... | #6 #187 |
| 19 | No automated tests is expensive | Titan #20 #117 | PEMS has 1,403 tests but the dynamic engine needs its own independent suite. | #203 #1 |
| 20 | The owner may commit in another terminal | Titan Instructions 10 | Re-run git status before commit and stage only specific paths. | - |
| 21 | Editable permission matrix on unit/user forms | Titan #3 | A permission UI instead of fixed seeds. | #60 |
| 22 | Jalali + Gregorian calendar as a default view | Titan #179 | Essential for the Iranian domain. | #206 #33 |
| 23 | Offline-only assistant | Titan #181 #182 | The hosting decision for an offline LLM was already made. | #216 |
| 24 | Code-first means the app applies the schema itself | PEMS (2026-10-21) | PEMS had EF migrations but Program.cs only ran seeders, so every new environment depended on a hand-run SQL script. Now the app tells an install (empty) from an update (pending) and from an unmanaged schema (never touched). Create-database must run before anything that connects (Hangfire installs its schema on configuration), migrations run before seeders, non-model DDL (full-text) is also a migration, and a CI drift gate keeps model and migrations in step. Keep an off-switch for reviewed-script environments. | #309 #310 #311 |
| 25 | Give pipelines a details-free health endpoint for the schema | PEMS (2026-10-22) | A deploy that uploads files cannot see whether the first start installed the database. An anonymous /health/database (200 only when UpToDate, state name otherwise) lets the pipeline wait for the install/update and fail the run, without exposing connection strings or versions. Raise the IIS startupTimeLimit because the first run builds the whole schema. | #310 #312 |
| 26 | One settings contract: files for settings, files for secrets, nothing in code | PEMS (2026-10-23) | Settings were spread over appsettings.*.json, in-code '?? default' values and a built-in administrator password in a public repository. Keep one key set per environment in committed env files, secrets only in git-ignored files or environment variables, required keys that fail fast, and tests that keep key sets identical and scan the committed files for secrets. A dependency-free reader is needed where the configuration packages cannot be referenced (design-time EF factory). | #315 #316 #317 |
| 27 | Study the whole project before building a library | PEMS (2026-10-23) | A source-level audit gave hard numbers (2,587 hand-written inputs vs 202 bound fields, 342 tables, 632 inline styles, 4 offline violations) that sized the waves and exposed a rule breach. Keep the audit script and rerun it per wave; use its counts as a CI ratchet. | #29 #327 |
| 28 | Make the component contract technology-neutral | PEMS (2026-10-23) | Dynamic forms need metadata-driven rendering while 300 existing pages need MVC compatibility. Describing fields/columns/views as descriptors keeps the rendering technology (Razor components or View Components) replaceable. | #319 #321 |
| 29 | One owner per field between the plan file and the issue tracker | PEMS (2026-10-23) | Two systems drift unless each field has a single owner and a script compares them. Plan ID and issue key link the records; the tracker owns status, the plan owns scope; changes to the plan go through pull requests. | #335 #336 |
