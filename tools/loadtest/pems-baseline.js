// PEMS — Authenticated Load & Concurrency Baseline
//
// طبق دستورالعمل P0 Authenticated Load & Concurrency Baseline (۲۰۲۶-۰۸-۲۴): این اسکریپت
// تنها ابزار بارگذاری در این مخزن است (هیچ k6/JMeter/NBomber/Artillery موجودی پیدا نشد -
// طبق بند ۳ همین دستورالعمل، این ابزار جدید ساخته شد، نه یک چارچوب رقیب دوم).
//
// دو سناریوی موازی:
//   general_users  - جمعیت اصلی (۲۰۰ حساب loadtestNNN/EngineeringManager)، بین ۶ نوع صفحه‌ی
//                    واقعی (Dashboard/Document Register/Global Search/Document Detail/
//                    Transmittal/Equipment) در هر Iteration به‌طور تصادفی جابه‌جا می‌شود - شبیه
//                    یک کاربر واقعی که در طول Session بین صفحات مختلف حرکت می‌کند، نه فقط یک
//                    Endpoint ثابت.
//   audit_log_admin - جمعیت جدا و کوچک (۱۰ حساب loadtestadminNN/SystemAdmin)، چون AuditLog
//                    در تولید واقعی [Authorize(Roles=SystemAdmin)] است و هرگز با جمعیت عمومی
//                    همزمان نمی‌شود - همزمانی ثابت و کوچک واقعی‌تر از مقیاس‌دهی با بقیه است.
//
// ورود فقط یک‌بار به‌ازای هر VU انجام می‌شود (طبق بند ۴، سناریوی ۰۱: "Do not repeatedly create
// users during the load test... Prefer session reuse") - نه هر Iteration؛ Cookie Jar
// پیش‌فرض k6 (به‌ازای هر VU مجزا) این را برای بقیه‌ی Session نگه می‌دارد.
//
// سطح همزمانی از طریق متغیر محیطی VUS و مدت از طریق DURATION کنترل می‌شود تا هر مرحله
// (Smoke/20/50/100/150/200) با یک اجرای جدا و ایزوله‌ی k6 قابل مقایسه با DMV Snapshot دیتابیس
// باشد - طبق بند ۵ («تدریجی، نه پرش مستقیم به ۲۰۰»).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend, Rate } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5089';
// SCRUM-17: no password in source; use the one given to tools/LoadTestSeed (k6 run -e PEMS_LOADTEST_PASSWORD=...).
const PASSWORD = __ENV.PEMS_LOADTEST_PASSWORD;
if (!PASSWORD) {
    throw new Error('Set PEMS_LOADTEST_PASSWORD (k6 run -e PEMS_LOADTEST_PASSWORD=...).');
}
const GENERAL_USER_COUNT = 200;
const ADMIN_USER_COUNT = 10;

const VUS = parseInt(__ENV.VUS || '2', 10);
const DURATION = __ENV.DURATION || '90s';
const RAMP = __ENV.RAMP || '20s';
const ADMIN_VUS = Math.min(parseInt(__ENV.ADMIN_VUS || '3', 10), ADMIN_USER_COUNT);

// اسناد واقعی PEMSDB (نه داده‌ی ساختگی) طبق بند ۵ سناریوی ۰۵ - انتخاب‌شده از طریق کوئری مستقیم
// روی DocumentRevisions.GROUP BY: سند ۲۶ رویژنی (J-80-PI-DWG-1300-0045 - duplicate) دقیقاً
// همان سندی است که در دستورالعمل ذکر شده بود.
const DOC_LOW = 21221;    // ۱ رویژن
const DOC_MEDIUM = 25700; // ۱۰ رویژن
const DOC_HIGH = 24382;   // ۲۶ رویژن - سند حساس AsSplitQuery

const EQUIPMENT_ID = 14619;
const TRANSMITTAL_ID = 6260;

export const errorRate = new Rate('pems_error_rate');
const loginTrend = new Trend('pems_login_duration', true);
const dashboardTrend = new Trend('pems_dashboard_duration', true);
const registerTrend = new Trend('pems_document_register_duration', true);
const searchTrend = new Trend('pems_global_search_duration', true);
const detailTrend = new Trend('pems_document_detail_duration', true);
const workflowTrend = new Trend('pems_document_workflow_duration', true);
const transmittalTrend = new Trend('pems_transmittal_duration', true);
const equipmentTrend = new Trend('pems_equipment_duration', true);
const auditLogTrend = new Trend('pems_audit_log_duration', true);
const loginFailures = new Counter('pems_login_failures');

export const options = {
    // SCRUM-535: keep the login cookie across iterations; k6 clears the cookie jar on every iteration by default, which makes the
    // one-login-per-VU session fail with 401 from the second iteration on.
    noCookiesReset: true,
    scenarios: {
        general_users: {
            executor: 'ramping-vus',
            exec: 'generalSession',
            startVUs: 0,
            stages: [
                { duration: RAMP, target: VUS },
                { duration: DURATION, target: VUS },
                { duration: '10s', target: 0 },
            ],
            gracefulRampDown: '10s',
        },
        audit_log_admin: {
            executor: 'ramping-vus',
            exec: 'auditLogSession',
            startVUs: 0,
            stages: [
                { duration: RAMP, target: ADMIN_VUS },
                { duration: DURATION, target: ADMIN_VUS },
                { duration: '10s', target: 0 },
            ],
            gracefulRampDown: '10s',
        },
    },
    thresholds: {
        // آستانه‌های Baseline (طبق تجربه‌ی صنعتی برای CRUD سازمانی، نه اختراع دلبخواه) - شکست
        // این آستانه به‌معنای Fail خودکار Build نیست، فقط در خلاصه‌ی k6 پرچم‌گذاری می‌شود تا در
        // گزارش نهایی بدون ابهام دیده شود.
        http_req_failed: ['rate<0.01'],
        pems_error_rate: ['rate<0.01'],
        http_req_duration: ['p(95)<3000'],
    },
};

function extractAntiforgeryToken(html) {
    const match = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]*)"/);
    return match ? match[1] : '';
}

function login(username) {
    const loginPageRes = http.get(`${BASE}/Account/Login`, { tags: { name: 'GET /Account/Login' } });
    const token = extractAntiforgeryToken(loginPageRes.body);

    const start = Date.now();
    const res = http.post(
        `${BASE}/Account/Login`,
        {
            __RequestVerificationToken: token,
            UserName: username,
            Password: PASSWORD,
            RememberMe: 'false',
        },
        { tags: { name: 'POST /Account/Login' }, redirects: 0 }
    );
    loginTrend.add(Date.now() - start);

    const ok = check(res, { 'login succeeded (302)': (r) => r.status === 302 });
    if (!ok) loginFailures.add(1);
    errorRate.add(!ok);
    return ok;
}

// طبق درخواست بند ۴ سناریوی ۰۱: ورود فقط یک‌بار به‌ازای هر VU - نه هر Iteration.
let sessionUser = null;

function ensureLoggedIn(userPrefix, userCount) {
    if (sessionUser) return true;
    const vuSlot = ((__VU - 1) % userCount) + 1;
    const padded = String(vuSlot).padStart(userPrefix === 'loadtestadmin' ? 2 : 3, '0');
    const username = `${userPrefix}${padded}`;
    const ok = login(username);
    if (ok) sessionUser = username;
    return ok;
}

function timedGet(url, trend, name) {
    const start = Date.now();
    const res = http.get(url, { tags: { name } });
    trend.add(Date.now() - start);
    const ok = check(res, { [`${name} => 200`]: (r) => r.status === 200 });
    errorRate.add(!ok);
    return res;
}

export function generalSession() {
    if (!ensureLoggedIn('loadtest', GENERAL_USER_COUNT)) {
        sleep(1);
        return;
    }

    // طبق سناریوی ۰۲: Dashboard احرازشده
    timedGet(`${BASE}/`, dashboardTrend, 'Scenario02_Dashboard');
    sleep(randomThinkTime());

    // طبق سناریوی ۰۳: Document Register - صفحه‌ی اول، Pagination، فیلتر پروژه/دیسیپلین/وضعیت،
    // جست‌وجوی شماره/عنوان مدرک - Query سمت سرور واقعی
    const registerVariant = randomInt(0, 4);
    let registerUrl = `${BASE}/DocumentRegister?page=1`;
    if (registerVariant === 1) registerUrl = `${BASE}/DocumentRegister?page=2`;
    else if (registerVariant === 2) registerUrl = `${BASE}/DocumentRegister?status=Issued&page=1`;
    else if (registerVariant === 3) registerUrl = `${BASE}/DocumentRegister?q=PI-DWG&page=1`;
    timedGet(registerUrl, registerTrend, 'Scenario03_DocumentRegister');
    sleep(randomThinkTime());

    // طبق سناریوی ۰۴: Global Search - واژه‌ی رایج، واژه‌ی نادر، شماره‌ی مدرک، بخشی از عنوان
    const searchTerms = ['PI', 'DWG', 'J-80-PI-DWG-1300-0045', 'Datasheet', 'valve'];
    const term = searchTerms[randomInt(0, searchTerms.length - 1)];
    timedGet(`${BASE}/GlobalSearch?q=${encodeURIComponent(term)}`, searchTrend, 'Scenario04_GlobalSearch');
    sleep(randomThinkTime());

    // طبق سناریوی ۰۵: Document Detail - کم/متوسط/زیاد رویژن (شامل سند حساس AsSplitQuery)
    const docVariant = randomInt(0, 2);
    const docId = docVariant === 0 ? DOC_LOW : docVariant === 1 ? DOC_MEDIUM : DOC_HIGH;
    timedGet(`${BASE}/EngineeringDocuments/Details/${docId}`, detailTrend, 'Scenario05_DocumentDetail');
    sleep(randomThinkTime());

    // طبق سناریوی ۰۶: Document Workflow - فقط مسیر خواندنی (تب History/Workflow همان صفحه‌ی
    // Detail) - طبق بند صریح دستورالعمل از فراخوانی SubmitAsync/ApproveAsync/... در بار بالا
    // خودداری شد تا تاریخچه‌ی گردش‌کار غیرقابل‌کنترل روی PEMSDB واقعی تولید نشود.
    timedGet(`${BASE}/EngineeringDocuments/Details/${DOC_HIGH}#workflow`, workflowTrend, 'Scenario06_WorkflowReadOnly');
    sleep(randomThinkTime());

    // طبق سناریوی ۰۷: Transmittal - فهرست و جزئیات (فقط خواندنی)
    if (randomInt(0, 1) === 0) {
        timedGet(`${BASE}/Transmittal`, transmittalTrend, 'Scenario07_TransmittalList');
    } else {
        timedGet(`${BASE}/Transmittal/Details/${TRANSMITTAL_ID}`, transmittalTrend, 'Scenario07_TransmittalDetail');
    }
    sleep(randomThinkTime());

    // طبق سناریوی ۰۸: Equipment - فهرست/جست‌وجو/جزئیات
    if (randomInt(0, 1) === 0) {
        timedGet(`${BASE}/Equipment?q=PI`, equipmentTrend, 'Scenario08_EquipmentList');
    } else {
        timedGet(`${BASE}/Equipment/Details/${EQUIPMENT_ID}`, equipmentTrend, 'Scenario08_EquipmentDetail');
    }
    sleep(randomThinkTime());
}

export function auditLogSession() {
    if (!ensureLoggedIn('loadtestadmin', ADMIN_USER_COUNT)) {
        sleep(1);
        return;
    }

    // طبق سناریوی ۰۹: بدون فیلتر، فیلتر EntityName، فیلتر UserName، مرتب‌سازی زمانی - دقیقاً
    // همان جدولی که به‌تازگی ایندکس‌گذاری شد (AddAuditLogPerformanceIndexes)
    const variant = randomInt(0, 3);
    let url = `${BASE}/Admin/AuditLog?page=1`;
    if (variant === 1) url = `${BASE}/Admin/AuditLog?entityName=EngineeringDocument&page=1`;
    else if (variant === 2) url = `${BASE}/Admin/AuditLog?userName=admin&page=1`;
    else if (variant === 3) url = `${BASE}/Admin/AuditLog?page=2`;

    timedGet(url, auditLogTrend, 'Scenario09_AuditLog');
    sleep(randomThinkTime());
}

function randomInt(min, max) {
    return Math.floor(Math.random() * (max - min + 1)) + min;
}

function randomThinkTime() {
    // زمان فکر واقع‌گرایانه‌ی کاربر بین کلیک‌ها - نه Zero-Think که فقط ظرفیت خام سرور را
    // اندازه می‌گیرد، نه رفتار واقعی ۱۵۰ کاربر روزانه.
    return 1 + Math.random() * 2;
}
