# Configuration constants: setting, policy constant or domain rule

Task: plan row #318 (SCRUM-335), second pass after #315 (environment files). Date: 2026-10-26. Rules: Instructions 24 (settings in `env/*.env`) and 3/4 (engines once).
The scan covered `PEMS.Web/Program.cs`, `PEMS.Web/Configuration`, controllers, `PEMS.Application`, `PEMS.Persistence`, `PEMS.Infrastructure` and `PEMS.AI`
(numeric and string literals that look like options, limits, timeouts, sizes, cookie/HSTS/SignalR/Hangfire/Identity options). The file-store and upload folder
paths are task #379; the upload pipeline rules are #183 and #358; `AllowedHosts` is #317.

## 1. How to classify a literal (use this for every new one)

| Class | Test | Where it lives |
|---|---|---|
| **Setting** | The right value differs per environment or customer (host names, timeouts that depend on data volume or network, retention, policy a customer may tighten), or an operator must change it without a release. | `env/<name>.env` in **all six** files, read with `Required*`; the key-parity test enforces it. Secrets go to `*.secrets.env`. |
| **Policy constant** | A security or platform choice that is the same everywhere and must not be loosened by configuration (a weaker cookie flag is a vulnerability, not a preference). | In code with a comment, and listed in section 4. |
| **Domain rule** | A business rule or field limit of a module (length of a subject, batch size, retry count, maximum depth). | In the module's `*Rules` class or service; changed by a release, tested; later by the metadata/validation engine (#355-#356). |
| **Technical tuning** | A performance or protocol default with no customer need yet. | In code, listed in section 6 with the trigger that moves it to a setting. |

## 2. Moved to settings by this task

All values equal today's behaviour, so nothing changes until an operator edits them.

| Setting | Was | Read in | Values |
|---|---|---|---|
| `Hosting__HstsMaxAgeDays` | `app.UseHsts()` with the framework default | `Program.cs` (`AddHsts`) | 30 in every file; raise per customer once HTTPS is final |
| `Authentication__Cookie__SlidingExpiration` | `options.SlidingExpiration = true` | `Program.cs` (application cookie) | `true` in every file |
| `Database__MigrationCommandTimeoutMinutes` | `SetCommandTimeout(TimeSpan.FromMinutes(10))` | `DatabaseUpdateService` (passed from `Program.cs`) | 10 in every file; raise for a large customer database |
| `Authentication__Password__RequiredLength` (production-kangan) | 8 | existing setting | **12** in `env/production-kangan.env` (decision in section 3) |

## 3. Production password policy (audit finding S-5)

Decision for the product default and customer override, following NIST SP 800-63B and OWASP ASVS V2 (length matters more than composition rules; do not force
upper-case or symbols; do not expire passwords on a schedule):

- **Production installations** (`production-<customer>.env`): `RequiredLength=12`, `RequireDigit=true`, no upper-case or symbol requirement. Set in `env/production-kangan.env`; a new customer file starts from it.
- **Local, docker, demo, staging, uat**: unchanged (length 8) so test data, seeds and demos keep working. A test requires at least 8 everywhere and 12 in `production-kangan`.
- **Lockout** stays 5 attempts / 10 minutes in every file.
- **Per-customer override**: only in that customer's own env file, never lower than the production default without the owner's written decision.
- The first administrator password (`Seed__Admin__Password`) must satisfy the policy of its environment, so for production-kangan it is at least 12 characters; the secrets example says so.
- Still open (not in this task): a list of blocked common passwords and an optional second factor for administrators; see the licensing/SSO rows (#190, #343).

## 4. Policy constants that stay in code

| Where | Constant | Why it is not a setting |
|---|---|---|
| `Program.cs:234-237` | auth cookie `HttpOnly=true`, `IsEssential=true`, `SameSite=Lax`, `SecurePolicy=Always` (`SameAsRequest` only in Development, :195) | Security baseline; `Lax` is needed for the sign-in redirect flow; loosening it per customer would be a vulnerability |
| `Program.cs:209,211` | `RequireUniqueEmail=true`, `Lockout.AllowedForNewUsers=true` | Account integrity; the numbers (attempts, minutes) are settings |
| `Program.cs:145` | Hangfire `CompatibilityLevel.Version_180`, simple type serializer | Data format of the job storage; changing it needs a migration, not an operator decision |
| `Program.cs:81` | SignalR defaults | Library defaults, no customer need yet |
| `Program.cs:996,1005` | HSTS and HTTPS redirect are on outside Development | Platform rule (offline product still serves HTTPS inside the company); only the HSTS age is a setting |
| Global `[Authorize]` policy (`AddControllersWithViews`) | every page requires sign-in unless `[AllowAnonymous]` | Access model (rule R12) |

## 5. Domain rules that stay in code (not environment-dependent)

| Group | Where | Examples |
|---|---|---|
| Correspondence and letters | `PEMS.Application/Common/IncomingCorrespondenceRules.cs`, `LetterComposeRules.cs`, `LetterReferralRules.cs`, `CartableRules.cs`; `PEMS.Persistence/Servises/Letter*Service.cs` | subject/summary lengths, max files/links/recipients, attachment bytes (25 MB), batch sizes (200/500), contact and rule counts, thread depth |
| Imports and bulk operations | `EquipmentImportService`, `MDRImportService`, `VDRImportService`, `MrsImportService`, `StrategyAssignmentService`, `PartCatalogueAdminService` | commit flush batches (200), chunk sizes (100/1000), max rows (100,000), max assign batch (2,000) |
| Concurrency and retries | `MaterialStockService`, `StoreRequisitionLifecycleService`, `ReplenishmentRecommendationService`, `LetterNumberingService` | max posting/apply attempts (3, 8) around app locks |
| Analytics and AI limits | `ConditionMonitoringService`, `ReliabilityAnalyticsService`, `ReliabilityIntelligenceService`, `PEMS.AI/*` | trend points, repeat window (90 days), minimum sample, prompt character caps |
| Lists and paging | `DocumentRegisterFilter.MaxPageSize=200`, `PartCatalogue*Service.MaxTake` | one server-side paging contract (guideline chapter 01) |
| Field lengths | `TaskManagerService`, `MrsImportService`, `PartCatalogueSettingsService` | title/description/comment/material-code lengths; move to the field validation catalog (#355) when it exists |

## 6. Technical tuning kept in code, with the trigger that turns it into a setting

| Where | Literal | Trigger / owner |
|---|---|---|
| 8 `[RequestSizeLimit(...)]` attributes (5 MB to 110 MB): `MyProfileController` (2), `MOCController`, `TechnicalQueryController`, `PartCatalogueAdminController`, `PartCatalogueRequestController`, `StrategyImportController`, `DCCController` | upload request caps | Attributes need compile-time constants; per-field limits belong to the upload pipeline (#183) and file field validation (#358) |
| `MemoryCacheService`, `RedisCacheService` | default expiry 10 minutes; `NavigationMenuService` 10 minutes; `LetterComposeService` 5 minutes | A customer reports stale menus or cache pressure; add `Cache__*` settings with the service constructor change and its tests |
| `FtpStorageProviderClient` (10 s connect, 30 s read), `SftpStorageProviderClient` (10 s) | storage provider timeouts | Per-provider setting on the storage provider record when a customer's FTP is slower |
| `DatabaseInitializer` connect timeout minimum 15 s | start-up probe | A customer database that needs longer to wake up |
| `DocumentRegisterController.MaxExportRows=20000` | export cap | Reporting engine rows (export parity, #371) |
| Ollama HTTP timeout | already `Ollama__TimeoutMinutes` | - |

## 7. Already settings (from #315, unchanged)

Serilog level and retention, database bootstrap flags, Ollama URL/models/timeout, OCR paths and languages, FTP defaults, Fido2 domain/origins/drift, lockout, password policy,
cookie name and lifetime, security-stamp interval, culture list, first administrator name and e-mail, feature flags, letter reminders and letterhead.

## 8. Rule for the next change

A new literal that fits the first row of section 1 is added to **all six** `env/*.env` files in the same pull request, read with `Required*`, and covered by
`EnvironmentFilesTests`; anything else is classified in this document in the same pull request.
