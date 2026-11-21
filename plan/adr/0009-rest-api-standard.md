# ADR-0009: REST API standard: resource-oriented JSON under /api/v1, one contract for every client

- **Status:** Accepted (amended 2026-10-28)
- **Date:** 2026-10-28
- **Decided by:** Hamid Maleki (owner, Member A): direction, paging delegation, acceptance of the rules and the decimals change on 2026-10-28
- **Plan rows:** #431 (SCRUM-431, this ADR); applies to #12, #18, #28, #195, #363, #387, #434, #435, #436
- **Sources:** plan audit [2026-10-28-dynamic-restful-audit.md](../../docs/audits/2026-10-28-dynamic-restful-audit.md); owner messages 2026-10-28 ("API-first now, decide the split after #441", paging: "you decide")

## Context

- PEMS is a server-rendered ASP.NET Core MVC application. Measured on `main`: 147 controllers, none marked `[ApiController]`, 163 GET and 668 POST actions, no PUT/PATCH/DELETE, no OpenAPI, no ProblemDetails, no API versioning, 22 controller files returning ad hoc JSON.
- The platform direction is a dynamic PEMS: pages, forms and components are stored as definitions and edited from the UI (#430). The designers, integrations and a possible separate front end (#441) all need one stable, documented, testable contract.
- Constraints: offline product (no CDN, docs served locally), Persian and English (RTL, `TranslatableText`, ADR-0008), row-level security on every path (#178), one deployable, cookie sign-in with antiforgery for the browser.
- Plan row #12 planned `/api/d/{entity}` with no standard to follow.

## Decision

We build an **API-first** REST surface inside the current application under `/api/v1`, with OpenAPI 3.1 as the contract. MVC pages stay and are not rewritten for this; the API grows resource by resource (strangler, see the REST inventory #432). The rules:

1. **Resources, not actions.** Plural kebab-case nouns (`/work-orders`, `/definitions`), ids opaque, nesting one level deep only for ownership (`/work-orders/{id}/attachments`). No verbs in paths and no state change through GET. A business action that is not a field change is a sub-resource: `POST /work-orders/{id}/actions/approve`.
2. **HTTP verbs.** GET read (safe), POST create or action, PUT full replace, PATCH partial update as JSON Merge Patch (RFC 7396), DELETE remove (soft-deleted resources still answer 204 and stay in the audit trail).
3. **Status codes.** 200 read/update, 201 create with `Location`, 202 accepted for long work with `Location` of an operation resource, 204 no content, 400 malformed request, 401 not signed in, 403 not allowed, 404 not found *or not visible to this user*, 409 state conflict, 412 `If-Match` failed, 415 wrong media type, 422 valid request that fails validation, 428 `If-Match` missing, 429 rate limited.
4. **Concurrency.** Every single-resource GET returns a strong `ETag` (from the row version). PUT, PATCH and DELETE require `If-Match`: missing gives 428, stale gives 412. POST creates accept an `Idempotency-Key` header; a replay returns the first response, the same key with a different body gives 422.
5. **Errors.** `application/problem+json` (RFC 9457) with `type`, `title`, `status`, `detail`, `instance`, `traceId` and, for validation, an `errors` array of `{ pointer (JSON Pointer), code (constraint code), messageKey, message, params }` (the contract of #363). Messages follow `Accept-Language`; never a stack trace.
6. **Lists.** One query contract (paging decision below): `sort=-createdAt,name`, `filter[status]=Open`, `filter[amount][gte]=100`, `q=` text search, `fields=a,b`. Response: `{ "items": [...], "paging": { ... } }`.
7. **Versioning.** Major version in the path. Adding optional fields or endpoints is not breaking. A breaking change is `/api/v2` served next to v1 for at least two releases, announced with `Deprecation` and `Sunset` headers.
8. **Data and formats.** JSON properties are camelCase; enum values are the domain names as strings (`"Draft"`); instants are ISO 8601 UTC (`2026-10-28T09:30:00Z`), dates `2026-10-28`; numbers are canonical and never formatted (digits and calendars are display, ADR-0008); decimal fields are plain JSON numbers (owner decision 2026-10-28; fields that need more than about 15 significant digits are declared as strings in the OpenAPI document). Stored translations are the `TranslatableText` map `{ "fa": "...", "en": "..." }`; the definition store returns the whole map, business data returns the text resolved for `Accept-Language`.
9. **Security.** Every endpoint has an authorization policy (a test fails otherwise); row-level security applies to lists, counts and single reads. Browser calls use the sign-in cookie plus the antiforgery header on unsafe verbs; integrations use bearer tokens (#195) and no cookies. CORS is deny by default, never `*`. Authenticated responses are `Cache-Control: private, no-cache` (revalidated with the ETag).
10. **Allowed actions.** A single-resource response carries `allowedActions` (for example `["edit","approve"]`) computed from permissions and state, so a dynamic UI can hide what the user may not do. It is a hint; the server still enforces.
11. **Bulk and long-running work.** `POST /{resource}/bulk` returns one result per item. Long work returns 202 and `Location: /api/v1/operations/{id}`; `GET` on it shows state and result.
12. **Contract.** OpenAPI 3.1 is generated from the code, served offline at `/api/v1/openapi.json`, linted and snapshot-tested in CI (#436).

### Paging decision (owner delegated)

Two ways to page a list:

- **Offset paging** (`page=3&pageSize=25`): "give me rows 51 to 75". Simple, the user can jump to any page, a total count is easy. Costs: the database still walks past the first 50 rows, so very deep pages get slow; and if rows are added or removed while someone pages, a row can appear twice or be skipped.
- **Cursor paging** (`limit=100&cursor=...`): "give me the 100 rows after this one", where the cursor is an opaque token that remembers the last row's sort key. Always fast, stable while data changes. Costs: no jumping to page N, no cheap total count.

**We use both from one implementation.** Human grids default to offset with a total, capped at `pageSize ≤ 200` and the first 10,000 rows (beyond that the API answers 422 asking for a narrower filter or the cursor mode). Machine consumers (exports, synchronisation, integrations, "load more" on very large tables) use cursor mode, which needs a stable unique sort key and has no total. A list that also needs a count in cursor mode asks for it explicitly (`count=true`). The test in #12 (500,000 records, filter and sort under 500 ms) must pass in cursor mode at any depth.

Response shapes: offset `{ "mode": "offset", "page": 3, "pageSize": 25, "total": 1234 }`, cursor `{ "mode": "cursor", "limit": 100, "next": "opaque-or-null" }`.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Keep RPC-style POST per action and add JSON to it | No caching, no concurrency control, no uniform errors; every designer and client would invent its own conventions. |
| GraphQL | Authorization and row-level security per field, caching and query cost control are harder; heavier offline tooling; the data model is already resource-shaped. Can be revisited for read-only reporting. |
| OData | Powerful filter syntax but a large surface and tooling we would have to restrict; our filter needs are covered by the simple contract above. |
| JSON:API envelope | Verbose for the benefit; the plain contract above is enough and easier to test. |
| Offset paging only | Fails the 500,000-record requirement for deep pages and for exports. |
| Cursor paging only | Users of grids need page jumping and totals. |
| Separate API project now | Adds deployment complexity before the split decision (#441); the API is built so it can be moved out later. |

## Consequences

- Good: one contract for the designers, integrations and a possible separate front end; testable rules (gates #436); concurrency and error handling are uniform.
- Cost or risk: two paging modes to test; ETag and `If-Match` discipline in every write; the existing 147 controllers are not touched until their resource is migrated.
- Follow-up work (plan rows): #432 inventory, #435 walking skeleton, #434 definition store, #436 gates, #12 and #195 conform to this ADR; #363 error contract, #18 and #28 use the ETag rules; guideline chapter [08 REST API standard](../../docs/guidelines/08-rest-api-standard.md).

## Amendments

- 2026-10-28 (owner): decimals are plain JSON numbers, not strings. Reason: simpler JSON for every client. Cost accepted: JavaScript clients can lose precision beyond about 15 significant digits, so a field that needs more is declared as a string in the OpenAPI document and the contract tests check it.
