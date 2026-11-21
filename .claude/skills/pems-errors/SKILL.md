---
name: pems-errors
description: PEMS error handling: throw the taxonomy exceptions, call the server from scripts with PemsFetch, show failures with the trace id, never alert() or ex.Message.
---

# PEMS error handling (guideline 08 "One trace id", "Error taxonomy"; SCRUM-464, 467)

**Server.** Throw `PEMS.Application.Exceptions`: `NotFoundException`, `ConflictException(code, msg)` (`unique`, `concurrency`, `frozen`, `stage`), `PemsValidationException`, `ForbiddenException`, `UnavailableException(code, msg, retryAfter)`; never a bare `new Exception(...)`. `PemsErrorMapper.Describe(ex)` maps any exception (SQL 2601/2627, 1205, -2, concurrency, timeouts too) to kind, status and code, used by the API filter, `PemsExceptionHandler` and the error page. The exception message is for the log: never put `ex.Message`, a stack, SQL or a path in a response, page or toast. Every response carries `X-Trace-Id`; problem bodies carry `traceId`.

**Scripts.** `PemsFetch.request/json(url, opts)` instead of a bare `fetch(` (antiforgery header, timeout, abort on leaving, problem+json, safe retry for GET/HEAD only, one toast with the reference). `PemsErrors.show(message)` for a message that is not from a request. Never `alert(` or `confirm(` (`data-pems-confirm`, `PemsDialog`). A failure nobody catches is shown by the global handler.

**Texts.** Messages come from `Err_*` and `Status_*` resource keys in both languages, passed to scripts as `data-pems-error-*` on `<body>` by the layout; no literal text in JS.

**Tests.** `ErrorMapperTests`, `TraceIdTests`, `ConfirmScriptTests` (UI) guard the contract; add a mapper case when you add a code.
