// PEMS - REST API baseline (SCRUM-436, extends the load tests of #202)
//
// Read-only scenarios against /api/v1 with a signed-in cookie session, one login per VU (same approach as pems-baseline.js). Writes are
// deliberately not load tested on a real database. The budgets below are the per-endpoint p95 limits of docs/performance/API_PERFORMANCE_BASELINE.md;
// a breach is reported by k6 (non-zero exit) so a pipeline can fail on it.
//
//   k6 run -e BASE_URL=http://localhost:5089 -e PEMS_LOADTEST_PASSWORD=... -e VUS=5 -e DURATION=60s tools/loadtest/pems-api-baseline.js
//   add  --summary-export tools/loadtest/results/api_baseline_summary.json  to store the result.
//
// The account must hold the permissions DisciplineMaster:View and Definitions:View (the loadtest accounts are created by tools/LoadTestSeed).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5089';
const PASSWORD = __ENV.PEMS_LOADTEST_PASSWORD;
if (!PASSWORD) {
    throw new Error('Set PEMS_LOADTEST_PASSWORD (k6 run -e PEMS_LOADTEST_PASSWORD=...).');
}
const VUS = parseInt(__ENV.VUS || '5', 10);
const DURATION = __ENV.DURATION || '60s';
const USER_PREFIX = __ENV.USER_PREFIX || 'loadtest';

export const apiErrorRate = new Rate('pems_api_error_rate');
const loginFailures = new Counter('pems_api_login_failures');

export const options = {
    // SCRUM-535: keep the login cookie across iterations; k6 clears the cookie jar on every iteration by default, which makes the
    // one-login-per-VU session fail with 401 from the second iteration on.
    noCookiesReset: true,
    scenarios: {
        api_readers: {
            executor: 'constant-vus',
            vus: VUS,
            duration: DURATION,
        },
    },
    thresholds: {
        pems_api_error_rate: ['rate<0.01'],
        http_req_failed: ['rate<0.01'],
        // per-endpoint budgets (p95, milliseconds) - keep in step with docs/performance/API_PERFORMANCE_BASELINE.md
        'http_req_duration{name:GET /api/v1/disciplines}': ['p(95)<300'],
        'http_req_duration{name:GET /api/v1/disciplines/{id}}': ['p(95)<200'],
        'http_req_duration{name:GET /api/v1/definitions}': ['p(95)<300'],
        'http_req_duration{name:GET /api/v1/definitions/{id}}': ['p(95)<200'],
        'http_req_duration{name:GET /api/v1/openapi.json}': ['p(95)<500'],
    },
};

function tokenOf(html) {
    const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]*)"/);
    return m ? m[1] : '';
}

let loggedIn = false;
function ensureLogin() {
    if (loggedIn) return true;
    const page = http.get(`${BASE}/Account/Login`, { tags: { name: 'GET /Account/Login' } });
    const slot = String(((__VU - 1) % 200) + 1).padStart(3, '0');
    const res = http.post(`${BASE}/Account/Login`, {
        __RequestVerificationToken: tokenOf(page.body),
        UserName: `${USER_PREFIX}${slot}`,
        Password: PASSWORD,
        RememberMe: 'false',
    }, { tags: { name: 'POST /Account/Login' }, redirects: 0 });
    loggedIn = check(res, { 'login succeeded (302)': (r) => r.status === 302 });
    if (!loggedIn) loginFailures.add(1);
    apiErrorRate.add(!loggedIn);
    return loggedIn;
}

function get(path, name, expected = 200) {
    const res = http.get(`${BASE}${path}`, { tags: { name }, headers: { Accept: 'application/json' } });
    const ok = check(res, { [`${name} => ${expected}`]: (r) => r.status === expected });
    apiErrorRate.add(!ok);
    return res;
}

export default function () {
    if (!ensureLogin()) { sleep(1); return; }

    const list = get('/api/v1/disciplines?pageSize=25&sort=name', 'GET /api/v1/disciplines');
    let id = null;
    try { id = list.json('items.0.id'); } catch (e) { id = null; }
    if (id) get(`/api/v1/disciplines/${id}`, 'GET /api/v1/disciplines/{id}');

    const defs = get('/api/v1/definitions?pageSize=25&sort=key', 'GET /api/v1/definitions');
    let defId = null;
    try { defId = defs.json('items.0.id'); } catch (e) { defId = null; }
    if (defId) get(`/api/v1/definitions/${defId}`, 'GET /api/v1/definitions/{id}');

    get('/api/v1/openapi.json', 'GET /api/v1/openapi.json');
    sleep(0.5 + Math.random());
}
