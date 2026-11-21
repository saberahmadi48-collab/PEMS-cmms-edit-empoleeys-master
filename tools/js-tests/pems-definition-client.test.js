// SCRUM-790 (gap of SCRUM-454): the definition API client against a stub server. Runs pems-definition-client.js with a fake document and a fake fetch that answers like the API (200 with an ETag,
// 412, 422 with pointers, 401, 403, 500); checks the If-Match round trip, the standard events, the mapping of a 422 to field messages, the retry of reads only, the Idempotency-Key of creates and the
// autosave states. Node 18+, no packages, no browser.
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');
const same = (a, b, m) => assert.strictEqual(JSON.stringify(a), JSON.stringify(b), m);   // objects of the vm realm have other prototypes

const source = fs.readFileSync(path.join(__dirname, '..', '..', 'PEMS.UI', 'wwwroot', 'pems-definition-client.js'), 'utf8');

/** One client with a scripted stub server: answers(method, url, headers, body) returns { status, body?, etag? }. */
function setup(answers) {
    const requests = [];
    const events = [];
    const document = {
        querySelector: (selector) => (selector.indexOf('csrf-token') >= 0 ? { getAttribute: () => 'csrf-1' } : null),
        dispatchEvent: (e) => { events.push({ name: e.type, detail: e.detail }); },
    };
    function CustomEvent(type, init) { this.type = type; this.detail = init && init.detail; }
    const fetch = async (url, options) => {
        requests.push({ url, method: options.method, headers: options.headers, body: options.body === undefined ? undefined : JSON.parse(options.body) });
        const a = answers(options.method, url, options.headers, requests.length);
        const text = a.body === undefined ? '' : JSON.stringify(a.body);
        return { status: a.status, ok: a.status >= 200 && a.status < 300, text: async () => text, headers: { get: (n) => (n.toLowerCase() === 'etag' ? a.etag || null : null) } };
    };
    const context = { window: {}, document, CustomEvent, fetch, URLSearchParams, JSON, Promise, Date, Math, setTimeout, clearTimeout, String, Object, Array, crypto: { randomUUID: () => 'uuid-1' } };
    vm.createContext(context);
    vm.runInContext(source, context);
    return { client: context.window.PemsDefinitions.create({ base: '/api/v1/definitions' }), requests, events };
}

(async () => {
    // 200: the ETag of a read becomes the If-Match of the next write; the antiforgery token is sent
    {
        const { client, requests } = setup((m) => (m === 'GET' ? { status: 200, body: { info: { revision: 3 } }, etag: '"3"' } : { status: 200, body: { info: { revision: 4 } }, etag: '"4"' }));
        const read = await client.getDraft(12);
        assert.ok(read.ok && read.etag === '"3"');
        const saved = await client.saveDraft(12, { a: 1 }, 'note');
        assert.ok(saved.ok);
        assert.strictEqual(requests[1].headers['If-Match'], '"3"', 'the write carries the ETag of the read');
        assert.strictEqual(requests[1].headers['X-CSRF-TOKEN'], 'csrf-1');
        same(requests[1].body, { payload: { a: 1 }, changeSummary: 'note' });
        await client.saveDraft(12, { a: 2 });
        assert.strictEqual(requests[2].headers['If-Match'], '"4"', 'the ETag of the answer is used next');
    }

    // 412: one standard event, and the result says so
    {
        const { client, events } = setup((m) => (m === 'GET' ? { status: 200, body: {}, etag: '"1"' } : { status: 412, body: { title: 'Precondition failed' } }));
        await client.getDraft(5);
        const r = await client.saveDraft(5, {});
        assert.strictEqual(r.status, 412);
        same(events.map((e) => e.name), ['pems-definition-conflict']);
        same(events[0].detail, { resource: '/5/draft' });
    }

    // 422: the pointers become field messages next to the field
    {
        const { client, events } = setup(() => ({ status: 422, body: { title: 'Validation failed', errors: [{ pointer: '/payload/fields/0/name', code: 'required', message: 'Name is required.' }, { pointer: '/payload/fields/0/name', message: 'Too short.' }, { code: 'x' }] } }));
        const r = await client.saveDraft(7, {});
        assert.strictEqual(r.ok, false);
        assert.strictEqual(r.status, 422);
        same(r.fieldErrors['/payload/fields/0/name'], ['Name is required.', 'Too short.']);
        same(r.fieldErrors['/'], ['x'], 'an error without a pointer is shown for the whole payload');
        assert.strictEqual(r.summary, 'Validation failed');
        same(events, [], '422 raises no event');
    }

    // 401 and 403: the unauthorized event with the status, the page survives (the call answers, it does not throw)
    for (const status of [401, 403]) {
        const { client, events } = setup(() => ({ status, body: { title: 'No' } }));
        const r = await client.get(1);
        assert.strictEqual(r.status, status);
        same(events.map((e) => e.name), ['pems-definition-unauthorized']);
        same(events[0].detail, { status });
    }

    // only reads are retried (three times on a server error); a write is sent once
    {
        const { client, requests } = setup(() => ({ status: 500, body: { title: 'boom' } }));
        await client.get(1);
        assert.strictEqual(requests.length, 3, 'a read is tried three times');
        const before = requests.length;
        await client.publish(1);
        assert.strictEqual(requests.length - before, 1, 'a write is not repeated');
    }

    // a network failure of a read is reported as status 0 after the tries, not thrown
    {
        const context = { window: {}, document: { querySelector: () => null, dispatchEvent() {} }, CustomEvent: function () {}, fetch: async () => { throw new Error('offline'); }, URLSearchParams, JSON, Promise, setTimeout, clearTimeout, String, Object, Array, Date, Math };
        vm.createContext(context);
        vm.runInContext(source, context);
        const r = await context.window.PemsDefinitions.create({}).get(1);
        assert.strictEqual(r.ok, false);
        assert.strictEqual(r.status, 0);
        assert.ok(/offline/.test(r.summary));
    }

    // a create sends an Idempotency-Key and no If-Match
    {
        const { client, requests } = setup(() => ({ status: 201, body: { ids: [1] } }));
        await client.importPackage({ format: 'pems.definitions/1', items: [] });
        assert.strictEqual(requests[0].headers['Idempotency-Key'], 'uuid-1');
        assert.ok(!('If-Match' in requests[0].headers));
    }

    // autosave: unsaved while typing, saving then saved after the flush, conflict on 412, error otherwise
    {
        const states = [];
        const { client } = setup(() => ({ status: 200, body: {}, etag: '"9"' }));
        const auto = client.autosave(3, () => ({ edited: true }), { delay: 5, onState: (s) => states.push(s) });
        auto.touch();
        assert.strictEqual(auto.state(), 'unsaved');
        await auto.flush();
        same(states, ['unsaved', 'saving', 'saved']);
        const conflict = setup(() => ({ status: 412, body: {} }));
        const autoConflict = conflict.client.autosave(3, () => ({}), { onState: () => {} });
        await autoConflict.flush();
        assert.strictEqual(autoConflict.state(), 'conflict');
        const failing = setup(() => ({ status: 500, body: {} }));
        const autoError = failing.client.autosave(3, () => ({}), { onState: () => {}, onError: () => {} });
        await autoError.flush();
        assert.strictEqual(autoError.state(), 'error');
    }

    console.log('pems-definition-client: all cases passed');
})().catch((e) => { console.error(e); process.exit(1); });
