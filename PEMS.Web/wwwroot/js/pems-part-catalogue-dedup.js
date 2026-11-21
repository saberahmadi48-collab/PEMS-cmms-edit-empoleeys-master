/* Part Catalogue: search existing parts + MPN duplicate check (read-only; never merges anything).
   Server is the authority; this only shows candidates. DOM is built with textContent (no innerHTML). */
(function () {
    'use strict';
    // markup comes from the page's registered templates (script-templates component, pems-template.js), never from a string in this file
    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }

    function init() {
        var root = document.getElementById('pcDedup');
        if (!root || root.dataset.pemsInit === '1') return;
        root.dataset.pemsInit = '1';

        var out = document.getElementById('pcDedupResult');
        var queryInput = document.getElementById('pcDedupQuery');
        var searchBtn = document.getElementById('pcDedupSearch');
        var checkBtn = document.getElementById('pcDedupCheck');
        var txt = {
            none: root.dataset.txtNone || '',
            ambiguous: root.dataset.txtAmbiguous || '',
            inflight: root.dataset.txtInflight || '',
            error: root.dataset.txtError || ''
        };

        function clear() { while (out.firstChild) out.removeChild(out.firstChild); }
        // SCRUM-337: a skeleton in the result panel while the search runs
        function waiting() { window.PemsSkeleton.show(out, 'text', { lines: 3 }); }

        function note(message, cls) {
            var p = tpl('pcd-note', { text: message });
            if (cls) p.className = cls;
            out.appendChild(p);
        }

        function render(hits) {
            if (!hits.length) { note(txt.none); return; }
            var ul = tpl('pcd-list');
            hits.forEach(function (h) {
                ul.appendChild(tpl('pcd-item', {
                    code: h.partNumber || '',
                    meta: [h.matchKind, h.matchedOn, h.manufacturerName, h.manufacturerPartNumber].filter(Boolean).join(' · '),
                    description: h.description ? h.description : null
                }));
            });
            out.appendChild(ul);
        }

        function getJson(url) {
            return fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); });
        }

        function val(id) { var el = document.getElementById(id); return el ? el.value.trim() : ''; }

        searchBtn.addEventListener('click', function () {
            clear();
            var q = queryInput.value.trim();
            if (q.length < 2) return;
            waiting();
            getJson(root.dataset.urlExisting + '?q=' + encodeURIComponent(q))
                .then(function (hits) { clear(); render(hits); })
                .catch(function () { clear(); note(txt.error, 'text-danger'); });
        });

        queryInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); searchBtn.click(); }
        });

        checkBtn.addEventListener('click', function () {
            clear();
            var params = new URLSearchParams();
            params.set('manufacturerPartNumber', val('pc_ManufacturerPartNumber'));
            params.set('supplierPartNumber', val('pc_SupplierPartNumber'));
            var mfrId = document.querySelector('input[name="ManufacturerId"]');
            if (mfrId && mfrId.value) params.set('manufacturerId', mfrId.value);
            params.set('newManufacturerName', val('pc_NewManufacturerName'));
            if (root.dataset.requestId) params.set('excludeRequestId', root.dataset.requestId);
            waiting();
            getJson(root.dataset.urlDuplicates + '?' + params.toString())
                .then(function (res) {
                    clear();
                    if (res.isAmbiguous) note(txt.ambiguous, 'text-warning');
                    render(res.candidates || []);
                    if (res.inFlightRequestsWithSameMpn > 0) note(txt.inflight + ' ' + res.inFlightRequestsWithSameMpn, 'text-warning');
                })
                .catch(function () { clear(); note(txt.error, 'text-danger'); });
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
