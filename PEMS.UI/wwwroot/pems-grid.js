// PEMS.UI enterprise data grid (SCRUM-54, #32). <div data-pems-grid data-config="{...}"> is written by <pems-data-grid>; every text and column comes from the config.
// The rows are read from cfg.url one page at a time (GET ?page=&pageSize=&sort=&q=&f.col=op:value -> { items, total }) and only the rows in view are written, so 100,000 rows stay smooth
// (the scroll height is total x row height; browsers hold up to about 10 million pixels here, so some 250,000 rows at the default height). Sort (click, shift for more), column filters,
// column picker with pinning, density, selection with bulk actions (ids, or "all matching" with the query), export and saved views (per user, in the browser) are all here.
// All markup is a registered script template (dg-*); this file only fills it. Logical properties only, so the same grid works right-to-left.
(function () {
    'use strict';

    var OVERSCAN = 6;
    var FILTER_DELAY = 350;
    var CELL_OPS = { '>=': 'gte', '<=': 'lte', '>': 'gt', '<': 'lt', '!=': 'ne', '=': 'eq' };

    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }
    function slot(el, name) { return el.matches('[data-slot="' + name + '"]') ? el : el.querySelector('[data-slot="' + name + '"]'); }
    function fmt(text, n) { return String(text || '').split('{0}').join(String(n)); }
    function store(key, value) {
        try {
            if (value === undefined) { var raw = window.localStorage.getItem(key); return raw ? JSON.parse(raw) : null; }
            window.localStorage.setItem(key, JSON.stringify(value));
        } catch (e) { /* storage blocked: nothing is remembered */ }
        return null;
    }
    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }
    /** A form post to an action of the site (the answer is JSON); used for the saved views kept on the server (SCRUM-793). */
    function postForm(url, fields) {
        return fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Accept': 'application/json', 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token() },
            body: Object.keys(fields).map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(fields[k]); }).join('&') });
    }

    function init(root) {
        if (root.getAttribute('data-pems-grid-ready') !== null) return;
        root.setAttribute('data-pems-grid-ready', '');
        var cfg = JSON.parse(root.getAttribute('data-config'));
        var T = cfg.texts;
        var stateKey = 'PemsGrid.state.' + root.id;
        var viewsKey = cfg.viewsKey ? 'PemsGrid.views.' + cfg.viewsKey : null;

        var cols = cfg.columns.map(function (c) { return { key: c.key, actions: c.actions || [], header: c.header, type: c.type || 'text', width: c.width || 140, sortable: c.sortable !== false, filterable: c.filterable !== false, visible: !c.hidden, pinned: c.pinned || null, align: c.align || null }; });
        var state = { sort: [], filters: {}, search: '', density: cfg.density || 'comfortable' };
        var pages = {};                 // page number -> rows
        var inflight = {};
        var total = null;
        var selection = {};             // id -> true
        var allMatching = false;
        var failed = false;
        var generation = 0;             // a late answer of an older query is ignored

        var shell = tpl('dg-root', { search: { attrs: { placeholder: T.search, 'aria-label': T.search } }, columnsBtn: T.columns, densityBtn: T.density, viewsBtn: T.views, exportBtn: T.export, clearBtn: T.clearSelection });
        root.appendChild(shell);
        var scroller = slot(shell, 'scroller'), head = slot(shell, 'head'), filterRow = slot(shell, 'filters'), spacer = slot(shell, 'spacer'), rowsBox = slot(shell, 'rows');
        var statusEl = slot(shell, 'status'), panel = slot(shell, 'panel'), bulk = slot(shell, 'bulk'), searchEl = slot(shell, 'search');
        scroller.style.maxBlockSize = cfg.height + 'px';
        scroller.style.blockSize = cfg.height + 'px';
        if (!cfg.exportUrl) slot(shell, 'exportBtn').remove();
        if (!viewsKey) slot(shell, 'viewsBtn').remove();

        // ---- remembered layout (widths, visibility, pinning, density) of this user and grid
        var saved = store(stateKey);
        if (saved) applyView(saved, true);

        function snapshot() {
            return { cols: cols.map(function (c) { return { key: c.key, visible: c.visible, pinned: c.pinned, width: c.width }; }), sort: state.sort, filters: state.filters, search: state.search, density: state.density };
        }
        function applyView(v, quiet) {
            (v.cols || []).forEach(function (s) { var c = cols.filter(function (x) { return x.key === s.key; })[0]; if (c) { c.visible = s.visible; c.pinned = s.pinned; c.width = s.width || c.width; } });
            if (!quiet) { state.sort = v.sort || []; state.filters = v.filters || {}; state.search = v.search || ''; }
            state.density = v.density || state.density;
        }
        function remember() { store(stateKey, snapshot()); }

        function rowHeight() { return state.density === 'compact' ? Math.round(cfg.rowHeight * 0.72) : cfg.rowHeight; }
        function visibleCols() { return cols.filter(function (c) { return c.visible; }); }

        // ---- the column template and the pinned offsets
        function layoutColumns() {
            var vc = visibleCols(), parts = [], offsets = {}, startOffset = 0, endOffset = 0, i;
            if (cfg.selectable) { parts.push('44px'); startOffset = 44; }
            vc.forEach(function (c) { parts.push(c.width + 'px'); });
            vc.forEach(function (c) { if (c.pinned === 'start') { offsets[c.key] = startOffset; startOffset += c.width; } });
            for (i = vc.length - 1; i >= 0; i--) if (vc[i].pinned === 'end') { offsets[vc[i].key] = endOffset; endOffset += vc[i].width; }
            var width = parts.reduce(function (s, p) { return s + parseInt(p, 10); }, 0);
            [head, filterRow, rowsBox].forEach(function (el) { el.style.setProperty('--dg-cols', parts.join(' ')); el.style.inlineSize = width + 'px'; });
            scroller.classList.toggle('is-compact', state.density === 'compact');
            return offsets;
        }
        var offsets = {};
        function pinCell(el, col, extra) {
            if (col && col.pinned) {
                el.classList.add('is-pinned', col.pinned === 'start' ? 'is-pinned-start' : 'is-pinned-end');
                el.style.setProperty(col.pinned === 'start' ? 'inset-inline-start' : 'inset-inline-end', (offsets[col.key] || 0) + 'px');
            }
            if (extra) el.classList.add(extra);
        }
        function alignOf(c) { return c.align || (c.type === 'number' ? 'end' : 'start'); }

        // ---- header and filters
        function buildHeader() {
            head.textContent = ''; filterRow.textContent = '';
            if (cfg.selectable) {
                var h = tpl('dg-hcheck'); head.appendChild(h);
                var box = slot(h, 'check'); box.addEventListener('change', function () { toggleAllLoaded(box.checked); });
                filterRow.appendChild(tpl('dg-fblank'));
            }
            visibleCols().forEach(function (c) {
                var cell = tpl('dg-hcell', { label: c.header });
                var sorted = state.sort.filter(function (s) { return s.key === c.key; })[0];
                cell.setAttribute('aria-sort', sorted ? (sorted.desc ? 'descending' : 'ascending') : 'none');
                cell.classList.add('is-align-' + alignOf(c));
                if (sorted) cell.classList.add(sorted.desc ? 'is-desc' : 'is-asc');
                pinCell(cell, c);
                var label = slot(cell, 'label');
                if (!c.sortable) label.disabled = true;
                else label.addEventListener('click', function (e) { toggleSort(c.key, e.shiftKey); });
                slot(cell, 'resize').addEventListener('pointerdown', function (e) { startResize(e, c); });
                head.appendChild(cell);

                var f = tpl('dg-fcell', { input: { attrs: { 'aria-label': T.filter + ': ' + c.header, 'data-key': c.key } } });
                var input = slot(f, 'input');
                input.value = state.filters[c.key] || '';
                if (!c.filterable) input.disabled = true;
                var timer;
                input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(function () { if (input.value) state.filters[c.key] = input.value; else delete state.filters[c.key]; reload(); }, FILTER_DELAY); });
                pinCell(f, c);
                filterRow.appendChild(f);
            });
        }
        function toggleSort(key, add) {
            var cur = state.sort.filter(function (s) { return s.key === key; })[0];
            var next = !cur ? { key: key, desc: false } : !cur.desc ? { key: key, desc: true } : null;
            var rest = add ? state.sort.filter(function (s) { return s.key !== key; }) : [];
            state.sort = next ? rest.concat([next]) : rest;
            reload(true);
        }
        function startResize(e, c) {
            e.preventDefault();
            var startX = e.clientX, startW = c.width, dir = getComputedStyle(root).direction === 'rtl' ? -1 : 1;
            function move(ev) { c.width = Math.max(60, startW + (ev.clientX - startX) * dir); offsets = layoutColumns(); buildHeader(); draw(); }
            function up() { document.removeEventListener('pointermove', move); document.removeEventListener('pointerup', up); remember(); }
            document.addEventListener('pointermove', move); document.addEventListener('pointerup', up);
        }

        // ---- the query and the pages
        function query(page) {
            var p = new URLSearchParams();
            if (page) { p.set('page', String(page)); p.set('pageSize', String(cfg.pageSize)); }
            if (state.sort.length) p.set('sort', state.sort.map(function (s) { return (s.desc ? '-' : '') + s.key; }).join(','));
            if (state.search) p.set('q', state.search);
            Object.keys(state.filters).forEach(function (k) {
                var col = cols.filter(function (c) { return c.key === k; })[0], text = String(state.filters[k]).trim();
                if (!text) return;
                var m = /^(>=|<=|!=|>|<|=)\s*(.*)$/.exec(text);
                if (m && col && col.type !== 'text' && col.type !== 'badge') p.set('f.' + k, CELL_OPS[m[1]] + ':' + m[2]);
                else p.set('f.' + k, text);
            });
            return p;
        }
        function fetchPage(n) {
            if (pages[n] || inflight[n]) return;
            inflight[n] = true;
            var mine = generation;
            fetch(cfg.url + (cfg.url.indexOf('?') < 0 ? '?' : '&') + query(n).toString(), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
                .then(function (data) {
                    if (mine !== generation) return;
                    pages[n] = data.items || [];
                    if (total === null || n === 1) total = data.total;
                    failed = false;
                    draw();
                })
                .catch(function () { if (mine === generation) { failed = true; statusEl.textContent = T.error; } })
                .then(function () { delete inflight[n]; });
        }
        function reload(keepSelection) {
            generation++; pages = {}; inflight = {}; total = null; failed = false;
            if (!keepSelection) clearSelection();
            scroller.scrollTop = 0;
            offsets = layoutColumns(); buildHeader(); remember();
            draw();
            fetchPage(1);
        }

        // ---- the window of rows
        function rowAt(i) { var n = Math.floor(i / cfg.pageSize) + 1, page = pages[n]; if (!page) { fetchPage(n); return null; } return page[i % cfg.pageSize] || null; }
        function draw() {
            var rh = rowHeight(), count = total === null ? 0 : total;
            spacer.style.blockSize = (count * rh) + 'px';
            var first = Math.max(0, Math.floor(scroller.scrollTop / rh) - OVERSCAN);
            var last = Math.min(count - 1, Math.ceil((scroller.scrollTop + scroller.clientHeight) / rh) + OVERSCAN);
            rowsBox.textContent = '';
            rowsBox.style.transform = 'translateY(' + (first * rh) + 'px)';
            var vc = visibleCols();
            for (var i = first; i <= last; i++) {
                var item = rowAt(i), id = item ? String(item[cfg.idField]) : null;
                var row = tpl('dg-row');
                row.style.blockSize = rh + 'px';
                row.setAttribute('aria-rowindex', String(i + 2));
                if (!item) row.classList.add('is-loading');
                if (id !== null && selection[id]) { row.classList.add('is-selected'); row.setAttribute('aria-selected', 'true'); }
                if (cfg.selectable) {
                    var box = tpl('dg-check');
                    var cb = slot(box, 'check');
                    if (item) { cb.checked = !!selection[id] || allMatching; cb.addEventListener('change', (function (rid) { return function (e) { setSelected(rid, e.target.checked); }; })(id)); } else cb.disabled = true;
                    row.appendChild(box);
                }
                vc.forEach(function (c) { row.appendChild(cellFor(c, item)); });
                rowsBox.appendChild(row);
            }
            var shown = failed ? T.error : total === null ? T.loading : total === 0 ? T.empty : fmt(T.rows, total);
            statusEl.textContent = shown;
            updateBulk();
            var all = head.querySelector('[data-slot="check"]');
            if (all) { var n = Object.keys(selection).length; all.checked = allMatching || (n > 0 && n >= Math.min(total || 0, Object.keys(pages).length * cfg.pageSize)); all.indeterminate = !allMatching && n > 0 && !all.checked; }
        }
        // ---- row actions: an "actions" column of links (get) and posts (post: the id goes to the action, the answer is JSON, the grid reloads)
        function holds(expr, item) {
            if (!expr || !window.PemsExpr) return true;
            try { return window.PemsExpr.compile(expr, ['row']).condition({ row: item }); } catch (e) { return true; }
        }
        function runRowAction(a, url, id) {
            var go = function () {
                fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Accept': 'application/json', 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token() }, body: 'id=' + encodeURIComponent(id) })
                    .then(function (r) { return r.json().catch(function () { return {}; }).then(function (j) { return { ok: r.ok, body: j }; }); })
                    .then(function (r) { if (r.ok) { reload(); if (r.body && r.body.message) statusEl.textContent = r.body.message; } else statusEl.textContent = (r.body && r.body.error) || T.error; })
                    .catch(function () { statusEl.textContent = T.error; });
            };
            if (a.confirm && window.PemsDialog && typeof window.PemsDialog.confirm === 'function') window.PemsDialog.confirm(a.confirm).then(function (ok) { if (ok) go(); });
            else go();
        }
        function actionsCell(c, item) {
            var cell = tpl('dg-cell-actions'), box = slot(cell, 'actions');
            if (item) (c.actions || []).forEach(function (a) {
                if (!holds(a.showWhen, item)) return;
                var id = String(item[cfg.idField]), url = a.url.split('{id}').join(encodeURIComponent(id)).replace(/\{raw\.([A-Za-z_][A-Za-z0-9_]*)\}/g, function (m, p) { var v = item[p]; return typeof v === 'string' && v.charAt(0) === '/' && v.charAt(1) !== '/' ? v : ''; });   // {raw.field}: a path of this site the row carries (batch 5)
                var post = a.method === 'post', el = tpl(post ? 'dg-row-button' : 'dg-row-link'), node = slot(el, post ? 'button' : 'link');
                node.textContent = a.label;
                node.classList.add('btn-outline-' + (a.tone || 'secondary'));
                if (post) node.addEventListener('click', function () { runRowAction(a, url, id); }); else node.setAttribute('href', url);
                box.appendChild(el);
            });
            cell.classList.add('is-align-start');
            pinCell(cell, c);
            return cell;
        }
        function cellFor(c, item) {
            if (c.type === 'actions') return actionsCell(c, item);
            var v = item ? item[c.key] : null, cell;
            if (item && c.type === 'badge' && v != null && v !== '') { cell = tpl('dg-cell-badge', { text: String(v) }); }
            else {
                var text = '';
                if (item && v != null) {
                    if (c.type === 'bool') text = v ? '✓' : '';
                    else if (c.type === 'date') text = String(v).slice(0, 10);
                    else if (c.type === 'datetime') text = String(v).slice(0, 16).replace('T', ' ');
                    else if (c.type === 'number' && typeof v === 'number') text = v.toLocaleString('en');
                    else text = String(v);
                }
                cell = tpl('dg-cell', { text: text });
                if (text) cell.setAttribute('title', text);
            }
            cell.classList.add('is-align-' + alignOf(c));
            pinCell(cell, c);
            return cell;
        }
        var raf = 0;
        scroller.addEventListener('scroll', function () { if (!raf) raf = requestAnimationFrame(function () { raf = 0; draw(); }); });

        // ---- selection and bulk actions
        function setSelected(id, on) { if (on) selection[id] = true; else delete selection[id]; allMatching = false; draw(); }
        function toggleAllLoaded(on) {
            allMatching = false;
            if (!on) { selection = {}; draw(); return; }
            Object.keys(pages).forEach(function (n) { pages[n].forEach(function (r) { selection[String(r[cfg.idField])] = true; }); });
            draw();
        }
        function clearSelection() { selection = {}; allMatching = false; }
        function updateBulk() {
            var n = allMatching ? total : Object.keys(selection).length;
            bulk.hidden = !cfg.selectable || n === 0;
            if (bulk.hidden) return;
            slot(bulk, 'bulkText').textContent = fmt(T.selected, n);
            var holder = slot(bulk, 'bulkActions');
            holder.textContent = '';
            if (!allMatching && total && n < total) {
                var all = tpl('dg-bulk-link', { button: fmt(T.selectAllMatching, total) });
                slot(all, 'button').addEventListener('click', function () { allMatching = true; draw(); });
                holder.appendChild(all);
            }
            cfg.bulkActions.forEach(function (a) {
                var b = tpl('dg-bulk-action', { button: a.label });
                var btn = slot(b, 'button'); btn.classList.add('btn-' + (a.tone || 'primary'));
                btn.addEventListener('click', function () { runBulk(a); });
                holder.appendChild(b);
            });
        }
        function runBulk(a) {
            var go = function () {
                var body = { action: a.key, allMatching: allMatching, ids: allMatching ? null : Object.keys(selection), query: allMatching ? query(0).toString() : null };
                fetch(a.url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify(body) })
                    .then(function (r) { if (!r.ok) throw new Error(String(r.status)); clearSelection(); reload(); })
                    .catch(function () { statusEl.textContent = T.error; });
            };
            if (a.confirm && window.PemsDialog && typeof window.PemsDialog.confirm === 'function') window.PemsDialog.confirm(a.confirm).then(function (ok) { if (ok) go(); });
            else go();
        }

        // ---- panels: columns, views
        function openPanel(kind) {
            if (!panel.hidden && panel.getAttribute('data-kind') === kind) { panel.hidden = true; return; }
            panel.textContent = ''; panel.setAttribute('data-kind', kind); panel.hidden = false;
            if (kind === 'columns') cols.forEach(function (c) {
                var item = tpl('dg-menu-item', { text: c.header });
                var box = slot(item, 'box'); box.checked = c.visible;
                box.addEventListener('change', function () { c.visible = box.checked; offsets = layoutColumns(); buildHeader(); draw(); remember(); });
                var pin = slot(item, 'pin');
                pin.setAttribute('data-pin', c.pinned || 'none');
                pin.addEventListener('click', function () { c.pinned = !c.pinned ? 'start' : c.pinned === 'start' ? 'end' : null; pin.setAttribute('data-pin', c.pinned || 'none'); offsets = layoutColumns(); buildHeader(); draw(); remember(); });
                panel.appendChild(item);
            });
            if (kind === 'density') ['comfortable', 'compact'].forEach(function (d) {
                var item = tpl('dg-menu-choice', { text: d === 'compact' ? T.compact : T.comfortable });
                if (state.density === d) item.classList.add('is-on');
                item.addEventListener('click', function () { state.density = d; panel.hidden = true; offsets = layoutColumns(); draw(); remember(); });
                panel.appendChild(item);
            });
            if (kind === 'views') { drawViews(); if (!server.loaded) loadServerViews(function () { if (!panel.hidden && panel.getAttribute('data-kind') === 'views') drawViews(); }); }
        }
        // ---- saved views: the ones of this browser, and (SCRUM-793) the ones kept on the server per user and per role, which follow the user to another device; a view saved for a role is shared by its members
        var server = { views: [], roles: [], loaded: false };
        function loadServerViews(done) {
            fetch('/d/grid-views?f.viewsKey=eq:' + encodeURIComponent(cfg.viewsKey) + '&pageSize=100', { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
                .then(function (d) {
                    var items = d.items || [];
                    server.views = items.filter(function (i) { return i.kind === 'view'; });
                    server.roles = items.filter(function (i) { return i.kind === 'role'; });
                    server.loaded = true;
                })
                .catch(function () { server.loaded = false; })     // no answer or no right: the views of this browser still work
                .then(done);
        }
        function parseState(text) { try { return JSON.parse(text); } catch (e) { return null; } }
        function drawViews() {
            panel.textContent = '';
            var views = store(viewsKey) || {};
            Object.keys(views).sort().forEach(function (name) {
                var item = tpl('dg-view-item', { name: name, remove: T.delete });
                slot(item, 'name').addEventListener('click', function () { applyView(views[name], false); panel.hidden = true; searchEl.value = state.search; reload(); });
                slot(item, 'remove').addEventListener('click', function () { delete views[name]; store(viewsKey, views); drawViews(); });
                panel.appendChild(item);
            });
            server.views.forEach(function (v) {
                var label = v.scope === 'user' ? v.name : v.name + ' (' + v.scope.slice(5) + ')';
                var item = tpl('dg-view-item', { name: label, remove: T.delete });
                slot(item, 'name').addEventListener('click', function () { var st = parseState(v.state); if (!st) return; applyView(st, false); panel.hidden = true; searchEl.value = state.search; reload(); });
                slot(item, 'remove').addEventListener('click', function () {
                    postForm('/a/grid-view-delete', { viewsKey: cfg.viewsKey, name: v.name, scope: v.scope })
                        .then(function () { loadServerViews(drawViews); });
                });
                panel.appendChild(item);
            });
            var form = tpl('dg-view-save', { input: { attrs: { placeholder: T.viewName, 'aria-label': T.viewName } }, button: T.saveView });
            var input = slot(form, 'input');
            var scopeBox = null;
            if (server.loaded) {
                var picker = tpl('dg-view-scope');
                scopeBox = slot(picker, 'scope');
                var me = document.createElement('option'); me.value = 'user'; me.textContent = T.viewScopeMe || 'Only me'; scopeBox.appendChild(me);
                server.roles.forEach(function (r) { var o = document.createElement('option'); o.value = r.scope; o.textContent = r.name; scopeBox.appendChild(o); });
                panel.appendChild(picker);
            }
            slot(form, 'button').addEventListener('click', function () {
                var name = input.value.trim();
                if (!name) { input.focus(); return; }
                if (server.loaded) {
                    postForm('/a/grid-view-save', { viewsKey: cfg.viewsKey, name: name, scope: scopeBox ? scopeBox.value : 'user', state: JSON.stringify(snapshot()) })
                        .then(function (r) { if (!r.ok) { views[name] = snapshot(); store(viewsKey, views); } })      // refused or offline: keep it in this browser
                        .catch(function () { views[name] = snapshot(); store(viewsKey, views); })
                        .then(function () { loadServerViews(drawViews); });
                    return;
                }
                views[name] = snapshot(); store(viewsKey, views); drawViews();
            });
            panel.appendChild(form);
        }
        shell.addEventListener('click', function (e) {
            var act = e.target.closest('[data-act]');
            if (!act) return;
            var kind = act.getAttribute('data-act');
            if (kind === 'columns' || kind === 'density' || kind === 'views') openPanel(kind);
            else if (kind === 'export') act.setAttribute('href', cfg.exportUrl + (cfg.exportUrl.indexOf('?') < 0 ? '?' : '&') + query(0).toString());
            else if (kind === 'clearSel') { clearSelection(); draw(); }
        });
        document.addEventListener('click', function (e) { if (!panel.hidden && !shell.contains(e.target)) panel.hidden = true; });
        var searchTimer;
        searchEl.value = state.search;
        searchEl.addEventListener('input', function () { clearTimeout(searchTimer); searchTimer = setTimeout(function () { state.search = searchEl.value.trim(); reload(); }, FILTER_DELAY); });

        offsets = layoutColumns();
        buildHeader();
        draw();
        fetchPage(1);

        root.pemsGrid = { reload: function () { reload(); }, selected: function () { return Object.keys(selection); }, state: snapshot };
    }

    function scan(scope) { (scope || document).querySelectorAll('[data-pems-grid]').forEach(init); }
    function boot() {
        scan(document);
        new MutationObserver(function (list) { list.forEach(function (m) { m.addedNodes.forEach(function (n) { if (n.nodeType === 1) { if (n.matches && n.matches('[data-pems-grid]')) init(n); scan(n); } }); }); }).observe(document.body, { childList: true, subtree: true });
    }
    window.PemsGrid = { init: init, scan: scan };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
