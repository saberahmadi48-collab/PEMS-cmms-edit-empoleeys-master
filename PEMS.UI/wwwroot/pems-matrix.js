// PEMS.UI editable matrix (SCRUM-627): <div data-pems-matrix="/a/{action}"> is written by the matrix component. A changed cell (input or select with data-row and data-column) posts
// { row, column, value, id } as JSON to the stored action (antiforgery header); on failure the value is restored and the status line says so. No markup is written here.
(function () {
    'use strict';

    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }

    function attach(root) {
        if (root.dataset.pemsMatrixInit) return;
        root.dataset.pemsMatrixInit = '1';
        var url = root.getAttribute('data-pems-matrix');
        if (!url) return;
        var status = root.querySelector('.pems-matrix__status');
        function say(kind) { if (status) status.textContent = status.getAttribute('data-' + kind) || ''; }
        root.addEventListener('focusin', function (e) { var t = e.target; if (t.hasAttribute && t.hasAttribute('data-row')) t.setAttribute('data-before', t.value); });
        root.addEventListener('change', function (e) {
            var t = e.target;
            if (!t.hasAttribute || !t.hasAttribute('data-row')) return;
            var before = t.getAttribute('data-before') || '';
            say('saving');
            fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ row: t.getAttribute('data-row'), column: t.getAttribute('data-column'), value: t.value, id: t.getAttribute('data-id') || '' }) })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); t.setAttribute('data-before', t.value); say('saved'); })
                .catch(function () { t.value = before; say('error'); });
        });
    }
    function scan(scope) { (scope || document).querySelectorAll('[data-pems-matrix]').forEach(attach); }
    window.PemsMatrix = { scan: scan };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { scan(); }); else scan();
})();
