// SCRUM-39: the browser expression engine gives the answers of tools/js-tests/expression-cases.json, the same file the server tests run (ADR-0015). Node 18+, no packages.
'use strict';
const fs = require('fs');
const path = require('path');
const assert = require('assert');
const PemsExpr = require('../../PEMS.UI/wwwroot/pems-expr.js');

const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'expression-cases.json'), 'utf8'));
let failed = 0;
for (const c of fixture.cases) {
    try {
        const run = () => PemsExpr.compile(c.expr, fixture.roots).evaluate(fixture.context, fixture.now);
        if (c.error) {
            let code = null;
            try { run(); } catch (e) { code = e.code; }
            assert.strictEqual(code, c.error, 'expected the error "' + c.error + '" but got ' + code);
        } else {
            assert.strictEqual(JSON.stringify(run()), JSON.stringify(c.expect));
        }
    } catch (e) {
        failed++;
        console.error('FAIL ' + c.expr + ' -> ' + e.message);
    }
}

// the limits stop a hostile expression
const deep = '('.repeat(40) + '1' + ')'.repeat(40);
assert.strictEqual(catchCode(() => PemsExpr.compile(deep)), 'limit-depth');
assert.strictEqual(catchCode(() => PemsExpr.compile('1' + '+1'.repeat(400))), 'limit-nodes');
assert.strictEqual(catchCode(() => PemsExpr.compile('x'.repeat(2001))), 'limit-length');
function catchCode(f) { try { f(); } catch (e) { return e.code; } return null; }

if (failed) { console.error(failed + ' of ' + fixture.cases.length + ' cases failed'); process.exit(1); }
console.log('expression cases ok: ' + fixture.cases.length);
