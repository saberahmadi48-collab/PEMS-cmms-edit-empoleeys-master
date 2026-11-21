// Exhaustive check of PemsDate.jalaliToIso (PEMS.UI/wwwroot/pems-date.js) against PEMS.Tests/TestData/jalali-year-starts.json: every day of the Jalali years 1300 to 1500 (SCRUM-225).
// Run: node tools/js-tests/pems-date.test.js   (needs Node 18+ with ICU; PEMS.Tests JalaliDateTests runs it when node is on the PATH)
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..', '..');
global.window = {}; global.document = { readyState: 'complete', querySelectorAll: () => [], addEventListener() {}, body: null };
require(root + '/PEMS.UI/wwwroot/pems-date.js');
const T = window.PemsDate;
const starts = JSON.parse(fs.readFileSync(root + '/PEMS.Tests/TestData/jalali-year-starts.json', 'utf8')).starts;
const iso = ms => new Date(ms).toISOString().slice(0, 10);
let ok = 0, bad = 0, rejected = 0, wrongReject = 0;
for (let y = 1300; y < 1500; y++) {
  const s = Date.parse(starts[y] + 'T00:00:00Z'), next = Date.parse(starts[y + 1] + 'T00:00:00Z');
  const len = (next - s) / 86400000;
  for (let m = 1; m <= 12; m++) {
    const dim = m <= 6 ? 31 : m <= 11 ? 30 : (len === 366 ? 30 : 29);
    for (let d = 1; d <= 31; d++) {
      const got = T.jalaliToIso(y, m, d);
      if (d <= dim) {
        const doy = (m <= 6 ? (m - 1) * 31 : 186 + (m - 7) * 30) + d;
        const want = iso(s + (doy - 1) * 86400000);
        if (got === want) ok++; else { bad++; if (bad < 5) console.log('MISMATCH', y, m, d, got, want); }
      } else { if (got === '') rejected++; else { wrongReject++; if (wrongReject < 5) console.log('ACCEPTED IMPOSSIBLE', y, m, d, got); } }
    }
  }
}
const summary = { validDaysChecked: ok, mismatches: bad, impossibleRejected: rejected, impossibleAccepted: wrongReject };
console.log(summary);
console.log('typed:', T.parseTyped('۱۴۰۳/۱۲/۳۰'), T.parseTyped('1403-12-30'), T.parseTyped('1403.1.5'), T.parseTyped('03/12/30'), T.parseTyped('abc'));
console.log('1403/12/30 ->', T.jalaliToIso(1403, 12, 30), '| 1404/12/30 ->', JSON.stringify(T.jalaliToIso(1404, 12, 30)), '| 1407/12/30 ->', JSON.stringify(T.jalaliToIso(1407, 12, 30)), '| 1408/12/30 ->', T.jalaliToIso(1408, 12, 30));
const typedOk = JSON.stringify(T.parseTyped('۱۴۰۳/۱۲/۳۰')) === '[1403,12,30]' && T.parseTyped('03/12/30') === null && T.parseTyped('abc') === null;
const leapOk = T.jalaliToIso(1403, 12, 30) === '2025-03-20' && T.jalaliToIso(1404, 12, 30) === '' && T.jalaliToIso(1407, 12, 30) === '' && T.jalaliToIso(1408, 12, 30) === '2030-03-20';
if (bad || wrongReject || ok !== 73049 || !typedOk || !leapOk) { console.error('pems-date conversion test FAILED'); process.exit(1); }
console.log('pems-date conversion test passed');
