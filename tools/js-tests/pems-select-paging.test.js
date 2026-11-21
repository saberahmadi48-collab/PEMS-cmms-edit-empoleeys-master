// SCRUM-513: the searchable select reads a large server list one page at a time (Tom Select virtual_scroll): the first page for the typed text, the next page
// when the server says hasMore, and nothing more when it does not. Runs pems-select.js against a fake page and a fake Tom Select; Node 18+, no packages.
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');
const same = (a, b, m) => assert.strictEqual(JSON.stringify(a), JSON.stringify(b), m);   // objects of the vm realm have other prototypes

const source = fs.readFileSync(path.join(__dirname, '..', '..', 'PEMS.UI', 'wwwroot', 'pems-select.js'), 'utf8');
const captured = [];
const calls = [];
function FakeTomSelect(select, settings) { captured.push(settings); this.settings = settings; }
const body = { getAttribute: () => '' };
const context = {
    window: {}, document: {
        body, readyState: 'complete', querySelectorAll: () => [], addEventListener() {},
    },
    MutationObserver: function () { this.observe = () => {}; }, URL, URLSearchParams, location: { href: 'http://localhost/' }, Event: function () {},
    encodeURIComponent, Promise, Object, Array, Number, String,
    fetch: (url) => { calls.push(url); return Promise.resolve({ ok: true, json: () => Promise.resolve(context.nextResponse) }); },
};
context.window.TomSelect = FakeTomSelect;
vm.createContext(context);
vm.runInContext(source, context);

function remoteSelect(attrs) {
    return {
        options: [], multiple: false, id: '', classList: { contains: () => false },
        getAttribute: (n) => (n in attrs ? attrs[n] : null), hasAttribute: (n) => n in attrs, setAttribute() {}, addEventListener() {},
        closest: () => null, matches: () => false, querySelector: () => null,
    };
}

(async () => {
    context.window.PemsSelect.init(remoteSelect({ 'data-pems-select-source': 'Equipment' }));
    const settings = captured[0];
    same(settings.plugins, ['virtual_scroll'], 'the select asks for the scroll paging plugin');
    assert.ok(settings.shouldLoad('a') && !settings.shouldLoad(''), 'one typed character starts a search');
    assert.ok(/page=1/.test(settings.firstUrl('pump')) && /type=Equipment/.test(settings.firstUrl('pump')) && /q=pump/.test(settings.firstUrl('pump')), settings.firstUrl('pump'));

    // page 1 says hasMore: the next url asks for page 2 with the same text
    const nextUrls = [];
    const self = { getUrl: (q) => settings.firstUrl(q), setNextUrl: (q, url) => nextUrls.push(url) };
    context.nextResponse = { hasMore: true, items: [{ id: 7, label: 'P-101', subLabel: 'Pump' }] };
    const got = await new Promise((resolve) => settings.load.call(self, 'pump', resolve));
    same(got, [{ value: '7', text: 'P-101', sub: 'Pump' }]);
    assert.strictEqual(nextUrls.length, 1);
    assert.ok(/page=2/.test(nextUrls[0]) && /q=pump/.test(nextUrls[0]), nextUrls[0]);

    // the last page: no next url
    context.nextResponse = { hasMore: false, items: [] };
    nextUrls.length = 0;
    await new Promise((resolve) => settings.load.call({ getUrl: () => nextUrlWithPage(2), setNextUrl: (q, u) => nextUrls.push(u) }, 'pump', resolve));
    assert.strictEqual(nextUrls.length, 0, 'no more pages asked after the last one');
    function nextUrlWithPage(n) { return '/SearchApi?type=Equipment&q=pump&page=' + n + '&pageSize=20'; }

    // a page endpoint (data-pems-select-url) pages the same way, with q and page parameters
    context.window.PemsSelect.init(remoteSelect({ 'data-pems-select-url': '/Part/Lookup' }));
    const urlSettings = captured[1];
    same(urlSettings.plugins, ['virtual_scroll']);
    assert.strictEqual(urlSettings.firstUrl('bolt'), '/Part/Lookup?q=bolt');
    context.nextResponse = { hasMore: true, items: [{ value: 'a', text: 'A' }] };
    nextUrls.length = 0;
    await new Promise((resolve) => urlSettings.load.call({ getUrl: () => '/Part/Lookup?q=bolt', setNextUrl: (q, u) => nextUrls.push(u) }, 'bolt', resolve));
    same(nextUrls, ['/Part/Lookup?q=bolt&page=2']);
    console.log('select paging ok');
})().catch((e) => { console.error(e); process.exit(1); });
