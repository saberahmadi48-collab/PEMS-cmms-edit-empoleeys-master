// List page component (SCRUM-337; look from SCRUM-408), rendered by the <pems-list-*> tag helpers of PEMS.UI.
// Search opens over the toolbar title; an optional KPI row is hidden by default and remembered per browser; the grid keeps
// its column widths. Markup contract:
//   [data-lp-toolbar]  [data-lp-search-open]  form[data-lp-search] (hidden) with input + [data-lp-search-close]
//     form[data-lp-live-filter]: typing filters the rows of table.lp-table on the page (no submit)
//     input[data-pems-search] inside it: picking a result opens it, Enter without a pick submits the form
//   [data-lp-kpi-toggle] (aria-controls = id of the KPI row, data-storage-key = localStorage key, data-lp-open = shown by default)
//   table.lp-table[data-grid-key] (column resize, PemsDataGrid)   [data-lp-reset-columns="gridKey"]
//   [data-lp-bulk="name"] bulk bar: shown while input[data-lp-select] rows are ticked (input[data-lp-select-all] ticks all);
//     its forms get the ticked values as hidden inputs called "name" on submit; [data-lp-bulk-count] shows data-count-text
(function () {
    'use strict';

    function initSearch(toolbar) {
        var openBtn = toolbar.querySelector('[data-lp-search-open]');
        var form = toolbar.querySelector('form[data-lp-search]');
        if (!openBtn || !form) return;
        var input = form.querySelector('input[type="search"], input[type="text"]');
        var closeBtn = form.querySelector('[data-lp-search-close]');

        // live-filter: the rows already on the page are filtered as the user types, nothing is submitted
        if (input && form.hasAttribute('data-lp-live-filter')) {
            var rows = function () { return document.querySelectorAll('table.lp-table tbody tr'); };
            var apply = function () {
                var q = input.value.toLowerCase();
                rows().forEach(function (r) { r.style.display = r.textContent.toLowerCase().indexOf(q) >= 0 ? '' : 'none'; });
            };
            input.addEventListener('input', apply);
            form.addEventListener('submit', function (e) { e.preventDefault(); apply(); });
        }

        function open() {
            form.hidden = false;
            toolbar.classList.add('lp-searching');
            openBtn.setAttribute('aria-expanded', 'true');
            if (input) { input.focus(); input.select(); }
        }
        function close(returnFocus) {
            form.hidden = true;
            toolbar.classList.remove('lp-searching');
            openBtn.setAttribute('aria-expanded', 'false');
            if (returnFocus) openBtn.focus();
        }

        openBtn.addEventListener('click', function () { form.hidden ? open() : close(true); });
        if (closeBtn) closeBtn.addEventListener('click', function () { close(true); });
        if (input) {
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.stopPropagation(); close(true); }
            });
            // an active search term keeps the field open so the user sees what is filtered
            if (input.value) open();
            // the shared autocomplete (data-pems-search): a picked result opens that record, Enter without a pick searches
            if (input.hasAttribute('data-pems-search')) {
                input.addEventListener('pems:search-select', function (e) {
                    if (e.detail && e.detail.url) window.location.href = e.detail.url;
                });
                input.addEventListener('pems:search-enter', function () { form.submit(); });
            }
        }
    }

    function initGrid(table) {
        if (window.PemsDataGrid && typeof window.PemsDataGrid.initColumnResize === 'function')
            window.PemsDataGrid.initColumnResize(table.getAttribute('data-grid-key'));
    }

    function initResetColumns(btn) {
        btn.addEventListener('click', function () {
            if (window.PemsDataGrid && typeof window.PemsDataGrid.resetColumns === 'function')
                window.PemsDataGrid.resetColumns(btn.getAttribute('data-lp-reset-columns'));
        });
    }

    function initKpis(btn) {
        var row = document.getElementById(btn.getAttribute('aria-controls'));
        if (!row) return;
        var key = btn.getAttribute('data-storage-key');

        function apply(show) {
            row.hidden = !show;
            btn.setAttribute('aria-expanded', show ? 'true' : 'false');
            btn.classList.toggle('on', show);
        }

        var stored = null;
        try { stored = key ? window.localStorage.getItem(key) : null; } catch (e) { /* storage blocked: default stays hidden */ }
        // no stored choice: hidden, unless the page opens its KPI row by default (data-lp-open)
        apply(stored === null ? btn.getAttribute('data-lp-open') === '1' : stored === '1');

        btn.addEventListener('click', function () {
            var show = row.hidden;
            apply(show);
            try { if (key) window.localStorage.setItem(key, show ? '1' : '0'); } catch (e) { /* not remembered */ }
        });
    }

    function initBulk(bar) {
        var scope = bar.closest('.lp-panel') || document;
        var rows = Array.prototype.slice.call(scope.querySelectorAll('input[data-lp-select]'));
        var all = scope.querySelector('input[data-lp-select-all]');
        var count = bar.querySelector('[data-lp-bulk-count]');
        var name = bar.getAttribute('data-lp-bulk') || 'ids';
        var text = bar.getAttribute('data-count-text') || '{0}';

        function ticked() { return rows.filter(function (c) { return c.checked; }); }
        function update() {
            var n = ticked().length;
            bar.hidden = n === 0;
            if (count) count.textContent = text.replace('{0}', n);
            if (all) {
                all.checked = n > 0 && n === rows.length;
                all.indeterminate = n > 0 && n < rows.length;
            }
        }

        rows.forEach(function (c) { c.addEventListener('change', update); });
        if (all) all.addEventListener('change', function () {
            rows.forEach(function (c) { c.checked = all.checked; });
            update();
        });
        bar.querySelectorAll('form').forEach(function (form) {
            form.addEventListener('submit', function () {
                form.querySelectorAll('input[data-lp-bulk-id]').forEach(function (i) { i.remove(); });
                ticked().forEach(function (c) {
                    var i = document.createElement('input');
                    i.type = 'hidden'; i.name = name; i.value = c.value;
                    i.setAttribute('data-lp-bulk-id', '');
                    form.appendChild(i);
                });
            });
        });
        update();
    }

    function boot() {
        document.querySelectorAll('[data-lp-bulk]').forEach(initBulk);
        document.querySelectorAll('[data-lp-toolbar]').forEach(initSearch);
        document.querySelectorAll('[data-lp-kpi-toggle]').forEach(initKpis);
        document.querySelectorAll('table.lp-table[data-grid-key]').forEach(initGrid);
        document.querySelectorAll('[data-lp-reset-columns]').forEach(initResetColumns);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
