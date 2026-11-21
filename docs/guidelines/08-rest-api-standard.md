# 08 - REST API standard

Status: **Accepted - 2026-10-28** (plan row #431, [ADR-0009](../../plan/adr/0009-rest-api-standard.md)). Read this chapter when you add or change anything under `/api` or any JSON endpoint.
The ADR holds the reasons; this chapter holds the rules and one good and one bad example for each. The API quality gates (#436) will check what can be checked by machine.

| # | Rule | Good | Bad |
|---|---|---|---|
| 1 | Resources are plural kebab-case nouns, no verbs, one level of nesting for ownership | `GET /api/v1/work-orders/42/attachments` | `GET /api/v1/getWorkOrderAttachments?id=42` |
| 2 | Business actions are sub-resources, never GET | `POST /api/v1/work-orders/42/actions/approve` | `GET /api/v1/work-orders/42/approve` |
| 3 | Use the verb that matches: POST create, PUT replace, PATCH partial (JSON Merge Patch), DELETE remove | `PATCH /api/v1/definitions/7` with `{ "title": { "en": "Pumps" } }` | `POST /api/v1/definitions/7/update` |
| 4 | Create returns 201 with `Location`; long work returns 202 with an operation `Location` | `201 Created`, `Location: /api/v1/definitions/8` | `200 OK` with `{ "ok": true }` |
| 5 | Status codes mean what they say: 401 not signed in, 403 not allowed, 404 not found or not visible, 409 state conflict, 422 validation | `422` with a field error list | `200 OK` with `{ "error": "..." }` |
| 6 | Writes need `If-Match` with the ETag from the read; missing 428, stale 412 | `PUT` with `If-Match: "v12"` | `PUT` without it, last writer wins |
| 7 | Creates accept `Idempotency-Key`; a replay returns the first response | `POST` twice with the same key creates one record | a double click creates two records |
| 8 | Errors are `application/problem+json` (RFC 9457) with an `errors` list (`pointer`, `code`, `messageKey`, `message`, `params`) | `{ "type": "...", "status": 422, "errors": [ { "pointer": "/title/en", "code": "required" } ] }` | a stack trace or an HTML error page |
| 9 | Lists use the one query contract: `sort`, `filter[field]`, `q`, `fields`, and paging as below | `GET /work-orders?filter[status]=Open&sort=-createdAt&page=2&pageSize=25` | `GET /work-orders?statusOpen=1&orderby=created_desc&p=2` |
| 10 | Human grids page by offset (total, jump to page; `pageSize ≤ 200`, first 10,000 rows); exports, sync and very large tables page by cursor (`limit`, opaque `cursor`, no total) | `GET /work-orders?limit=100&cursor=eyJ...` for an export | `page=5000` on a 500,000-row table |
| 11 | JSON is camelCase; enum values are domain names as strings; instants are ISO 8601 UTC, dates `yyyy-MM-dd`; numbers are never formatted; decimals are plain JSON numbers (a field needing more than about 15 significant digits is declared a string in OpenAPI) | `{ "dueDate": "2026-10-28", "amount": 1250.50, "status": "Open" }` | `{ "DueDate": "۱۴۰۵/۰۸/۰۶", "amount": "1,250.50" }` (formatted text instead of a number) |
| 12 | Stored translations are the `TranslatableText` map; message text follows `Accept-Language` | `"title": { "fa": "پمپ‌ها", "en": "Pumps" }` | a language-specific field per language |
| 13 | Every endpoint has an authorization policy and applies row-level security to lists, counts and single reads | `[Authorize(Policy = "Definitions.Edit")]` | an endpoint with no attribute |
| 14 | Browser calls: cookie plus antiforgery header on unsafe verbs; integrations: bearer token, no cookie; CORS deny by default | `X-CSRF-TOKEN` header on `PATCH` | `Access-Control-Allow-Origin: *` |
| 15 | Single-resource responses carry `allowedActions` so a dynamic UI hides what the user may not do (the server still enforces) | `"allowedActions": ["edit", "approve"]` | the UI guesses from the role name |
| 16 | Version in the path; adding optional fields is not breaking; a breaking change is a new major served next to the old with `Deprecation` and `Sunset` headers | `/api/v1` and `/api/v2` side by side | renaming a field in v1 |
| 17 | The OpenAPI 3.1 document is generated from the code, served offline at `/api/v1/openapi.json`, and a CI snapshot fails on an unapproved change | change the code, update the approved snapshot in the same PR | edit the document by hand |

Display is not data: digits and calendars shown to a user are formatted by the page (ADR-0008, `IPemsLocale`); the API always sends canonical values.

When a rule does not fit, change the ADR first (a new ADR if it reverses a decision), then this chapter, then the code.

## One trace id (SCRUM-464)

Every response carries `X-Trace-Id`, the W3C trace id of the request (the server request id when no trace is active), and every problem+json body carries the same value as `traceId`. The same value is the `TraceId` property of every log line of the request and the reference shown on the error pages and in client toasts, so a support request that quotes it finds the log entry. Code: `PEMS.Web.Diagnostics.PemsTrace` (`UsePemsTraceId` runs before the exception handler, `ProblemTraceIdFilter` fills the body). Never invent a second reference.

## Error taxonomy and the one mapping table (SCRUM-464)

Throw the taxonomy of `PEMS.Application.Exceptions` (`NotFoundException`, `ConflictException`, `PemsValidationException`, `ForbiddenException`, `UnavailableException`; never a bare `new Exception(...)`). `PemsErrorMapper.Describe(exception)` (PEMS.Web) maps any exception to one kind, status and stable code; the API controller filter, the exception handler and the error page all use it, so they agree.

| Kind | Status | Codes today | Other exceptions mapped to it |
|---|---|---|---|
| NotFound | 404 | `not-found` | `KeyNotFoundException` |
| Conflict | 409 | `unique`, `concurrency`, `frozen`, `stage` (any code you pass) | SQL 2601/2627, `DbUpdateConcurrencyException`, `DocumentFrozenException` |
| Validation | 422 | `validation` (+ field errors) | |
| Forbidden | 403 | `forbidden` | `UnauthorizedAccessException` |
| Unavailable | 503 + `Retry-After` | `deadlock`, `timeout`, `database-unavailable`, `storage` | SQL 1205, -2, 53/233/4060/40613 ..., `TimeoutException` |
| Unexpected | 500 | `server-error` | everything else |

The exception message is for the log only: no response, page or toast shows it (the problem title is the generic sentence of the kind, the pages show the localised text of the status plus the trace id).

## Building an endpoint (the walking skeleton, SCRUM-435)

The shared plumbing lives in `PEMS.Web/Api`; the pilot resource `DisciplinesApiController` (`/api/v1/disciplines`) is the example to copy.

1. Derive from `ApiControllerBase` (it is `[ApiController]`, answers in `application/problem+json`) and give the class `[Route(Prefix + "/<plural-kebab-noun>")]`.
2. Mark **every** action with `[ApiPermission("<Module>", "<Action>")]` (the permission matrix, `IPermissionService`); a test fails for an API action without it or with `[AllowAnonymous]`.
3. Keep the controller thin: map HTTP to a service in `PEMS.Application`/`PEMS.Persistence` that the pages also use. Errors come from `ApiProblem` (never an ad hoc body); a field error needs a JSON Pointer and a message key.
4. Lists use `ApiListQuery` (and `ApiFields` for `fields`); single reads set the `ETag` from the row version; PUT, PATCH and DELETE call `ApiETag.Evaluate` before writing and pass the row version to the service so the check is repeated on the database.
5. A concurrency token needs a `[Timestamp] byte[] RowVersion` on the entity (new migration).
6. Run `PEMS_GOLDEN_RECORD=1 dotnet test PEMS.Tests --filter OpenApiDocumentTests` and commit the changed `PEMS.Tests/Api/Snapshots/openapi-v1.json` in the same PR.

Known gaps, tracked in other rows: the integration bearer token (issuing and validation, #195); the Idempotency-Key store is in memory and per server (durable store with #434); cursor paging (first export endpoint); HTTP-level tests of the real pipeline need SQL Server.

## Quality gates (SCRUM-436)

What fails, and where. Every API pull request runs all of it; a rule that is only a sentence in this chapter is not a rule.

| Gate | What it checks | Rules | Where |
| --- | --- | --- | --- |
| Spectral ruleset | the OpenAPI document: path form, no GET actions, 401/403 and the problem+json error bodies on every operation, If-Match + 412/428 on PUT/PATCH/DELETE, 201 on collection POST, camelCase names, declared security, summary and description | 1, 2, 4, 5, 6, 8, 11, 13, 14 | `tools/api-lint/.spectral.yaml`, run by `tools/api-lint/lint.sh` on the approved snapshot; `lint.sh selftest` proves each rule fires on `samples-bad-api.json` |
| OpenAPI snapshot | the document is exactly the approved one | 17 | `OpenApiDocumentTests` against `PEMS.Tests/Api/Snapshots/openapi-v1.json` |
| Policy gate | every API action has `[ApiPermission]`, none is anonymous, none opts out of the antiforgery check, routes are lower-case kebab-case | 1, 2, 13, 14 | `ApiPolicyGateTests` |
| Conformance suite | the real pipeline, for every operation of the document: anonymous gives 401, no permission gives 403, an unsafe verb without `X-CSRF-TOKEN` gives 400, an unknown id gives 404, a missing or stale `If-Match` gives 428 or 412, bad list queries give 422 with pointers, an exception gives a 500 without a stack trace; every error is `application/problem+json` with `status`, a `urn:pems:problem:` type, a title and an `errors` list | 1, 5, 6, 8, 9, 13, 14 | `ApiConformanceTests` (cases come from the document, so a new endpoint cannot skip them) |
| Performance | the in-process pipeline stays far under its budget; the k6 scenarios hold the per-endpoint p95 and error rate | - | `ApiPipelineBaselineTests`, `tools/loadtest/pems-api-baseline.js`, [budgets](../performance/API_PERFORMANCE_BASELINE.md) |

**Changing the API on purpose.** Change the code, run `PEMS_GOLDEN_RECORD=1 dotnet test PEMS.Tests --filter OpenApiDocumentTests`, read the diff of `openapi-v1.json`, run `tools/api-lint/lint.sh`, and commit the new snapshot in the same pull request. A snapshot diff that nobody can explain is a bug. Add the endpoint's budget to the performance document and the k6 script.

**Run them.** `dotnet test PEMS.Tests --filter "FullyQualifiedName~PEMS.Tests.Api|FullyQualifiedName~PEMS.Tests.Definitions"` and `tools/api-lint/lint.sh` (Node 18+; Spectral 6.15 is fetched by `npx`, Apache-2.0, a development tool that never ships). The manual build workflow runs both.

