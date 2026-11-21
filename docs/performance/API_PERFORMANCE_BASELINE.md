# API performance baseline and budgets (SCRUM-436)

Part of the performance suite of row #202. Applies to the REST API under `/api/v1` ([chapter 08](../guidelines/08-rest-api-standard.md)).

## 1. Budgets (p95 per endpoint, signed-in user, real database)

| Endpoint | p95 budget | Error rate | Notes |
| --- | --- | --- | --- |
| `GET /api/v1/disciplines?pageSize=25` | 300 ms | < 1 % | list, offset paging |
| `GET /api/v1/disciplines/{id}` | 200 ms | < 1 % | |
| `GET /api/v1/definitions?pageSize=25` | 300 ms | < 1 % | list, offset paging |
| `GET /api/v1/definitions/{id}` | 200 ms | < 1 % | envelope with its versions |
| `GET /api/v1/openapi.json` | 500 ms | < 1 % | generated per request today (about 35 ms in process); cache it if it ever shows up |

The same numbers are the thresholds of `tools/loadtest/pems-api-baseline.js`. A new endpoint gets a row here and a threshold there in the PR that adds it. The budgets are starting values: measure on the target hardware, then tighten them.

## 2. In-process baseline (stored, 2026-10-28)

Measured by `PEMS.Tests/Api/ApiPipelineBaselineTests.cs` in a cloud container: the real controllers, filters, permission policy and JSON, over the in-memory database, 200 requests per endpoint after a warm-up. It shows the cost of the pipeline itself, not of SQL Server, and guards against gross regressions (the test fails above 250 ms p95).

| Endpoint | p50 | p95 | Errors |
| --- | --- | --- | --- |
| `GET /api/v1/disciplines?pageSize=25&sort=name` | 0.47 ms | 1.00 ms | 0 / 200 |
| `GET /api/v1/disciplines/1` | 0.36 ms | 0.56 ms | 0 / 200 |
| `GET /api/v1/definitions?pageSize=25&sort=key` | 1.21 ms | 1.63 ms | 0 / 200 |
| `GET /api/v1/openapi.json` | 35.7 ms | 58.4 ms | 0 / 200 |

## 3. k6 baseline against a real server (SCRUM-535)

Measured on 2026-11-19 against isolated local SQL Server Express QA525 on an Intel Core Ultra 7 155H (16 cores, 22 logical processors), with five dedicated read-only accounts for 60 seconds. The run completed 263 iterations and 1,325 HTTP requests with zero failures. All five endpoints, including both detail endpoints, passed their budgets.

| Endpoint | p95 | Budget |
| --- | ---: | ---: |
| Disciplines list | 70.77 ms | 300 ms |
| Discipline details | 44.31 ms | 200 ms |
| Definitions list | 40.56 ms | 300 ms |
| Definition details | 41.37 ms | 200 ms |
| OpenAPI JSON | 55.07 ms | 500 ms |

The one-login-per-VU session persisted throughout the run. The full report is in [api_baseline_summary.json](../../tools/loadtest/results/api_baseline_summary.json). See manual check **M-27** in the [README](../../README.md) for the procedure.

```
k6 run -e BASE_URL=http://localhost:5089 -e PEMS_LOADTEST_PASSWORD=... -e VUS=5 -e DURATION=60s \
  --summary-export tools/loadtest/results/api_baseline_summary.json tools/loadtest/pems-api-baseline.js
```

Store `api_baseline_summary.json` (p95 per endpoint, error rate, requests per second) next to the other stage results and copy the p95 figures into a table here with the date and the hardware. The accounts need the permissions `DisciplineMaster:View` and `Definitions:View`.
