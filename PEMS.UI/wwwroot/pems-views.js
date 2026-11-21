// PEMS.UI view types (SCRUM-55, #33): cards, kanban, calendar (Jalali and Gregorian) and timeline. <div data-pems-view data-config="{...}"> is written by <pems-data-view>; the rows come from the
// same endpoint as the data grid (GET ?page&pageSize&sort&q&f.col=op:value -> { items, total }), and the view is configured by the field names of the entity (title, date, group).
// Calendar: the month grid of the Jalali or the Gregorian calendar (switchable), the first day of the week from the config; Jalali parts come from the browser Intl persian calendar, the
// same source the date picker and the expression engine are proven against. Kanban: dragging a card (or Alt+Left/Right on a focused card) posts { id, to } to cfg.moveUrl.
// All markup is a registered script template (dv-*); this file only fills it. Logical properties only, so the same views work right-to-left.
(function () {
    'use strict';

    var DAY = 86400000;
    var MAX_PER_DAY = 3;
    var BOARD_LIMIT = 500;
    var MAX_DAYS = 366;

    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }
    function slot(el, name) { return el.matches('[data-slot="' + name + '"]') ? el : el.querySelector('[data-slot="' + name + '"]'); }
    function fmt(text, n) { return String(text || '').split('{0}').join(String(n)); }
    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }
    function iso(d) { return d.toISOString().slice(0, 10); }
    function addDays(d, n) { return new Date(d.getTime() + n * DAY); }
    function utc(y, m, d) { return new Date(Date.UTC(y, m, d)); }
    function dateKey(v) { return v == null ? '' : String(v).slice(0, 10); }

    // ---- the two calendars (Jalali parts from Intl, the Gregorian ones from the date itself)
    var persian = typeof Intl !== 'undefined' ? new Intl.DateTimeFormat('en-u-ca-persian-nu-latn', { year: 'numeric', month: 'numeric', day: 'numeric', timeZone: 'UTC' }) : null;
    function jparts(d) {
        var o = {};
        persian.formatToParts(d).forEach(function (p) { if (p.type === 'year' || p.type === 'month' || p.type === 'day') o[p.type] = +p.value; });
        return o;
    }
    // the first day of a Jalali month as a UTC date: start near it, walk back before the month, then forward to its first day
    function jFirst(jy, jm) {
        var target = jy * 12 + jm, g = addDays(utc(jy + 621, 2, 21), Math.round((jm - 1) * 30.5)), i, p;
        for (i = 0; i < 70; i++) { p = jparts(g); if (p.year * 12 + p.month >= target) g = addDays(g, -1); else break; }
        for (i = 0; i < 70; i++) { p = jparts(g); if (p.year * 12 + p.month === target && p.day === 1) return g; g = addDays(g, 1); }
        return g;
    }
    function system(cfg, sys) { return sys === 'jalali' ? 'fa-u-ca-persian-nu-latn' : (cfg.locale === 'fa' ? 'fa-u-ca-gregory-nu-latn' : 'en-u-ca-gregory-nu-latn'); }

    function init(root) {
        if (root.getAttribute('data-pems-view-ready') !== null) return;
        root.setAttribute('data-pems-view-ready', '');
        var cfg = JSON.parse(root.getAttribute('data-config'));
        var T = cfg.texts, F = cfg.fields;
        var shell = tpl('dv-root', { prev: T.previous, today: T.today, next: T.next, jalali: T.jalali, gregorian: T.gregorian });
        root.appendChild(shell);
        var body = slot(shell, 'body'), statusEl = slot(shell, 'status'), titleEl = slot(shell, 'title');
        body.style.maxBlockSize = cfg.height + 'px';
        var isCal = cfg.view === 'calendar';
        if (!isCal) { slot(shell, 'toolbar').hidden = true; }
        var generation = 0;
        var sys = cfg.calendar === 'jalali' ? 'jalali' : 'gregorian';
        var anchor = new Date();                       // the month shown (UTC date)
        anchor = utc(anchor.getFullYear(), anchor.getMonth(), anchor.getDate());

        function href(item) { return cfg.itemUrl ? cfg.itemUrl.split('{id}').join(encodeURIComponent(String(item[F.id || 'id']))) : null; }
        function request(extra) {
            var p = new URLSearchParams(extra || {});
            return fetch(cfg.url + (cfg.url.indexOf('?') < 0 ? '?' : '&') + p.toString(), { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); });
        }
        function say(text) { statusEl.textContent = text || ''; }
        function fail(mine) { if (mine === generation) say(T.error); }

        // ---- cards
        function cardFor(item) {
            var c = tpl('dv-card', { title: String(item[F.title] == null ? '' : item[F.title]), subtitle: F.subtitle && item[F.subtitle] != null ? String(item[F.subtitle]) : null, badge: F.badge && item[F.badge] != null && item[F.badge] !== '' ? String(item[F.badge]) : null, image: F.image && item[F.image] ? { attrs: { src: String(item[F.image]) } } : null });
            var h = href(item);
            if (h) c.setAttribute('href', h); else c.removeAttribute('href');
            c.setAttribute('data-id', String(item[F.id || 'id']));
            return c;
        }
        function paged(render, sortKey) {
            var page = 1, total = null, holder = null, moreBtn = null;
            function load() {
                var mine = ++generation; say(T.loading);
                var q = { page: String(page), pageSize: String(cfg.pageSize) };
                if (sortKey) q.sort = sortKey;
                request(q).then(function (data) {
                    if (mine !== generation) return;
                    total = data.total; if (!holder) { holder = render.start(body); }
                    render.add(holder, data.items || []);
                    var shown = page * cfg.pageSize;
                    if (moreBtn) { moreBtn.remove(); moreBtn = null; }
                    if (total === 0) say(T.empty); else say('');
                    if (shown < total) {
                        var m = tpl('dv-more', { button: T.more });
                        slot(m, 'button').addEventListener('click', function () { page++; load(); });
                        body.appendChild(m); moreBtn = m;
                    }
                }).catch(function () { fail(mine); });
            }
            body.textContent = ''; load();
        }
        function showCards() {
            paged({
                start: function (b) { var w = tpl('dv-cards'); b.appendChild(w); return w; },
                add: function (w, items) { items.forEach(function (it) { w.appendChild(cardFor(it)); }); }
            });
        }

        // ---- timeline: rows by date, newest last, grouped by day
        function showTimeline() {
            var dayLists = {};
            var fmtDay = new Intl.DateTimeFormat(system(cfg, 'gregorian') + (cfg.locale === 'fa' ? '' : ''), { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC' });
            paged({
                start: function (b) { var w = tpl('dv-timeline'); b.appendChild(w); return w; },
                add: function (w, items) {
                    items.forEach(function (it) {
                        var key = dateKey(F.date ? it[F.date] : ''), list = dayLists[key];
                        if (!list) {
                            var day = tpl('dv-tl-day', { date: key ? fmtDay.format(new Date(key + 'T00:00:00Z')) : T.uncategorized });
                            w.appendChild(day); list = dayLists[key] = slot(day, 'list');
                        }
                        list.appendChild(cardFor(it));
                    });
                }
            }, F.date || null);
        }

        // ---- gantt: one request, a bar per row from its start to its end date on a shared day scale (at most MAX_DAYS wide)
        function showGantt() {
            var mine = ++generation; say(T.loading); body.textContent = '';
            var q = { page: '1', pageSize: String(BOARD_LIMIT) };
            if (F.date) q.sort = F.date;
            request(q).then(function (data) {
                if (mine !== generation) return;
                var rows = [], lo = null, hi = null;
                (data.items || []).forEach(function (it) {
                    var s = dateKey(it[F.date]); if (!s) return;
                    var e = dateKey(F.end ? it[F.end] : '') || s;
                    var sd = new Date(s + 'T00:00:00Z'), ed = new Date(e + 'T00:00:00Z');
                    if (isNaN(sd) || isNaN(ed)) return;
                    if (ed < sd) ed = sd;
                    rows.push({ item: it, s: sd, e: ed });
                    if (lo === null || sd < lo) lo = sd;
                    if (hi === null || ed > hi) hi = ed;
                });
                if (!rows.length) { say(T.empty); return; }
                var days = Math.min(MAX_DAYS, Math.round((hi - lo) / DAY) + 1), end = addDays(lo, days);
                var wrap = tpl('dv-gantt'), scale = slot(wrap, 'scale'), list = slot(wrap, 'rows');
                var fmtTick = new Intl.DateTimeFormat(system(cfg, sys), { month: 'short', day: 'numeric', timeZone: 'UTC' });
                var step = Math.max(1, Math.ceil(days / 8));
                for (var d = 0; d < days; d += step) {
                    var tick = tpl('dv-gantt-tick', { tick: fmtTick.format(addDays(lo, d)) });
                    tick.style.setProperty('--from', String(d / days * 100));
                    scale.appendChild(tick);
                }
                rows.forEach(function (r) {
                    if (r.s >= end) return;
                    var row = tpl('dv-gantt-row'), label = slot(row, 'label'), bar = slot(row, 'bar');
                    label.textContent = String(r.item[F.title] == null ? '' : r.item[F.title]);
                    var link = href(r.item); if (link) { label.setAttribute('href', link); bar.setAttribute('href', link); }
                    var from = Math.round((r.s - lo) / DAY), span = Math.min(days - from, Math.round((r.e - r.s) / DAY) + 1);
                    bar.style.setProperty('--from', String(from / days * 100));
                    bar.style.setProperty('--span', String(span / days * 100));
                    bar.setAttribute('title', label.textContent + ' (' + dateKey(r.item[F.date]) + (F.end && r.item[F.end] ? ' - ' + dateKey(r.item[F.end]) : '') + ')');
                    list.appendChild(row);
                });
                body.appendChild(wrap);
                say('');
            }).catch(function () { fail(mine); });
        }

        // ---- tree: one request, rows nested by the parent field; a node with children has a toggle (collapsed below the first level)
        function showTree() {
            var mine = ++generation; say(T.loading); body.textContent = '';
            var q = { page: '1', pageSize: String(BOARD_LIMIT) };
            request(q).then(function (data) {
                if (mine !== generation) return;
                var items = data.items || [], byId = {}, kids = {}, roots = [];
                items.forEach(function (it) { byId[String(it[F.id || 'id'])] = it; });
                items.forEach(function (it) {
                    var p = F.parent && it[F.parent] != null ? String(it[F.parent]) : '';
                    if (p && byId[p] && p !== String(it[F.id || 'id'])) (kids[p] = kids[p] || []).push(it); else roots.push(it);
                });
                if (!items.length) { say(T.empty); return; }
                var seen = {};
                function node(it, level) {
                    var id = String(it[F.id || 'id']);
                    if (seen[id]) return null;                    // a cycle in the data is cut, never followed twice
                    seen[id] = true;
                    var li = tpl('dv-tree-node'), title = slot(li, 'title'), toggle = slot(li, 'toggle'), badge = slot(li, 'badge'), list = slot(li, 'children');
                    title.textContent = String(it[F.title] == null ? '' : it[F.title]);
                    var link = href(it); if (link) title.setAttribute('href', link);
                    if (F.badge && it[F.badge] != null && it[F.badge] !== '') badge.textContent = String(it[F.badge]); else badge.hidden = true;
                    li.setAttribute('aria-level', String(level));
                    var children = (kids[id] || []).map(function (c) { return node(c, level + 1); }).filter(Boolean);
                    if (!children.length) { toggle.hidden = true; list.hidden = true; return li; }
                    children.forEach(function (c) { list.appendChild(c); });
                    var open = level < 2;
                    function set(v) { open = v; list.hidden = !v; li.setAttribute('aria-expanded', v ? 'true' : 'false'); toggle.textContent = v ? T.collapse : T.expand; toggle.setAttribute('aria-label', toggle.textContent); }
                    toggle.addEventListener('click', function () { set(!open); });
                    set(open);
                    return li;
                }
                var tree = tpl('dv-tree');
                roots.forEach(function (r) { var n = node(r, 1); if (n) tree.appendChild(n); });
                body.appendChild(tree);
                say('');
            }).catch(function () { fail(mine); });
        }

        // ---- kanban: one request for the board, grouped by the group field
        function showBoard() {
            var mine = ++generation; say(T.loading); body.textContent = '';
            request({ page: '1', pageSize: String(BOARD_LIMIT) }).then(function (data) {
                if (mine !== generation) return;
                var items = data.items || [], board = tpl('dv-board'), cols = {};
                var known = {};
                cfg.groups.forEach(function (g) { known[g.value] = true; });
                var defs = cfg.groups.slice();
                if (items.some(function (it) { return !known[String(it[F.group])]; })) defs.push({ value: '', label: T.uncategorized, tone: 'secondary' });
                defs.forEach(function (g) {
                    var col = tpl('dv-col', { label: g.label, count: '0' });
                    col.setAttribute('data-value', g.value); col.classList.add('is-' + (g.tone || 'secondary'));
                    cols[g.value] = col; board.appendChild(col);
                    if (cfg.moveUrl) wireDrop(col, g.value);
                });
                items.forEach(function (it) {
                    var v = String(it[F.group] == null ? '' : it[F.group]);
                    var col = cols[known[v] ? v : ''];
                    var card = cardFor(it);
                    if (cfg.moveUrl) wireDrag(card);
                    slot(col, 'list').appendChild(card);
                });
                body.appendChild(board);
                counts(board);
                say(items.length === 0 ? T.empty : (data.total > items.length ? '' : ''));
            }).catch(function () { fail(mine); });
        }
        function counts(board) { board.querySelectorAll('.pems-view__col').forEach(function (c) { slot(c, 'count').textContent = String(slot(c, 'list').children.length); }); }
        function moveCard(card, col) {
            var from = card.parentNode, board = col.closest('.pems-view__board');
            if (from === slot(col, 'list')) return;
            slot(col, 'list').appendChild(card); counts(board);
            fetch(cfg.moveUrl, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify({ id: card.getAttribute('data-id'), to: col.getAttribute('data-value') }) })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); })
                .catch(function () { from.appendChild(card); counts(board); say(T.error); });
        }
        function wireDrag(card) {
            card.setAttribute('draggable', 'true');
            card.addEventListener('dragstart', function (e) { e.dataTransfer.setData('text/plain', card.getAttribute('data-id')); card.classList.add('is-dragging'); root.__dragging = card; });
            card.addEventListener('dragend', function () { card.classList.remove('is-dragging'); root.__dragging = null; });
            card.addEventListener('keydown', function (e) {
                if (!e.altKey || (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight')) return;
                e.preventDefault();
                var cols = Array.prototype.slice.call(card.closest('.pems-view__board').querySelectorAll('.pems-view__col'));
                var at = cols.indexOf(card.closest('.pems-view__col'));
                var rtl = getComputedStyle(root).direction === 'rtl';
                var step = (e.key === 'ArrowRight') !== rtl ? 1 : -1;
                var next = cols[at + step];
                if (next) { moveCard(card, next); card.focus(); }
            });
        }
        function wireDrop(col) {
            col.addEventListener('dragover', function (e) { if (root.__dragging) { e.preventDefault(); col.classList.add('is-over'); } });
            col.addEventListener('dragleave', function () { col.classList.remove('is-over'); });
            col.addEventListener('drop', function (e) { e.preventDefault(); col.classList.remove('is-over'); if (root.__dragging) moveCard(root.__dragging, col); });
        }

        // ---- calendar: moving an item to another day posts { id, date } (JSON) to cfg.moveUrl, an /a/{action} of a stored definition (SCRUM-772); Alt+arrow keys move it by a day or a week
        function moveChip(chip, cell) {
            var from = chip.parentNode, target = slot(cell, 'items');
            if (!target || from === target) return;
            target.appendChild(chip);
            fetch(cfg.moveUrl, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify({ id: chip.getAttribute('data-id'), date: cell.getAttribute('data-date') }) })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); })
                .catch(function () { from.appendChild(chip); say(T.error); });
        }
        function wireChip(chip, item) {
            chip.setAttribute('data-id', String(item[F.id] == null ? '' : item[F.id]));
            chip.setAttribute('draggable', 'true');
            chip.addEventListener('dragstart', function (e) { e.dataTransfer.setData('text/plain', chip.getAttribute('data-id')); root.__dragging = chip; });
            chip.addEventListener('dragend', function () { root.__dragging = null; });
            chip.addEventListener('keydown', function (e) {
                var steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
                if (!e.altKey || !(e.key in steps)) return;
                e.preventDefault();
                var rtl = getComputedStyle(root).direction === 'rtl';
                var n = steps[e.key]; if (rtl && Math.abs(n) === 1) n = -n;
                var cell = chip.closest('[data-date]'); if (!cell) return;
                var parts = cell.getAttribute('data-date').split('-');
                var next = root.querySelector('[data-date="' + iso(addDays(utc(+parts[0], +parts[1] - 1, +parts[2]), n)) + '"]');
                if (next) { moveChip(chip, next); chip.focus(); }
            });
        }
        function wireDay(cell) {
            cell.addEventListener('dragover', function (e) { if (root.__dragging) { e.preventDefault(); cell.classList.add('is-over'); } });
            cell.addEventListener('dragleave', function () { cell.classList.remove('is-over'); });
            cell.addEventListener('drop', function (e) { e.preventDefault(); cell.classList.remove('is-over'); if (root.__dragging) moveChip(root.__dragging, cell); });
        }

        // ---- calendar
        function monthGrid() {
            var start, first, inMonth, label;
            var fmtTitle = new Intl.DateTimeFormat(system(cfg, sys), { month: 'long', year: 'numeric', timeZone: 'UTC' });
            if (sys === 'jalali') {
                var p = jparts(anchor); first = jFirst(p.year, p.month);
                var nextY = p.month === 12 ? p.year + 1 : p.year, nextM = p.month === 12 ? 1 : p.month + 1;
                var after = jFirst(nextY, nextM);
                inMonth = function (d) { var q = jparts(d); return q.year === p.year && q.month === p.month; };
                label = function (d) { return jparts(d).day; };
                titleEl.textContent = fmtTitle.format(first);
                return build(first, inMonth, label, after);
            }
            first = utc(anchor.getUTCFullYear(), anchor.getUTCMonth(), 1);
            inMonth = function (d) { return d.getUTCMonth() === first.getUTCMonth(); };
            label = function (d) { return d.getUTCDate(); };
            titleEl.textContent = fmtTitle.format(first);
            return build(first, inMonth, label, utc(first.getUTCFullYear(), first.getUTCMonth() + 1, 1));
            function build(firstDay, within, dayLabel, afterDay) {
                var lead = (firstDay.getUTCDay() - cfg.weekStart + 7) % 7;
                start = addDays(firstDay, -lead);
                return { start: start, days: 42, within: within, dayLabel: dayLabel };
            }
        }
        function showCalendar() {
            var mine = ++generation; say(T.loading);
            var g = monthGrid(), from = iso(g.start), to = iso(addDays(g.start, g.days));
            var q = { page: '1', pageSize: '500' };
            q['f.' + F.date] = 'range:' + from + '..' + to;
            request(q).then(function (data) {
                if (mine !== generation) return;
                var byDay = {};
                (data.items || []).forEach(function (it) { var k = dateKey(it[F.date]); (byDay[k] = byDay[k] || []).push(it); });
                var cal = tpl('dv-cal'), week = slot(cal, 'weekdays'), grid = slot(cal, 'grid');
                var names = new Intl.DateTimeFormat(system(cfg, sys), { weekday: 'short', timeZone: 'UTC' });
                for (var i = 0; i < 7; i++) week.appendChild(tpl('dv-cal-wd', { text: names.format(addDays(g.start, i)) }));
                var todayKey = iso(new Date());
                for (var d = 0; d < g.days; d++) {
                    var day = addDays(g.start, d), key = iso(day), items = byDay[key] || [];
                    var cell = tpl('dv-cal-day', { num: String(g.dayLabel(day)) });
                    if (!g.within(day)) cell.classList.add('is-other');
                    if (key === todayKey) cell.classList.add('is-today');
                    cell.setAttribute('data-date', key);
                    if (cfg.moveUrl && F.date) wireDay(cell);
                    var list = slot(cell, 'items');
                    items.slice(0, MAX_PER_DAY).forEach(function (it) { list.appendChild(chipFor(it)); });
                    if (items.length > MAX_PER_DAY) {
                        (function (rest, holder) {
                            var more = tpl('dv-more-items', { button: fmt(T.moreItems, rest.length) });
                            slot(more, 'button').addEventListener('click', function () { more.remove(); rest.forEach(function (it) { holder.appendChild(chipFor(it)); }); });
                            holder.appendChild(more);
                        })(items.slice(MAX_PER_DAY), list);
                    }
                    grid.appendChild(cell);
                }
                body.textContent = ''; body.appendChild(cal);
                say(data.items && data.items.length ? '' : T.empty);
            }).catch(function () { fail(mine); });
        }
        function chipFor(item) {
            var c = tpl('dv-chip', { chip: String(item[F.title] == null ? '' : item[F.title]) });
            if (cfg.moveUrl && F.date) wireChip(c, item);
            var h = href(item);
            if (h) c.setAttribute('href', h); else c.removeAttribute('href');
            c.setAttribute('title', c.textContent);
            return c;
        }
        function shift(n) {
            if (sys === 'jalali') { var p = jparts(anchor), m = p.year * 12 + p.month - 1 + n; anchor = jFirst(Math.floor(m / 12), (m % 12) + 1); }
            else anchor = utc(anchor.getUTCFullYear(), anchor.getUTCMonth() + n, 1);
            showCalendar();
        }
        shell.addEventListener('click', function (e) {
            var act = e.target.closest('[data-act]');
            if (!act || !isCal) return;
            var kind = act.getAttribute('data-act');
            if (kind === 'prev') shift(-1);
            else if (kind === 'next') shift(1);
            else if (kind === 'today') { var n = new Date(); anchor = utc(n.getFullYear(), n.getMonth(), n.getDate()); showCalendar(); }
            else if (kind === 'jalali' || kind === 'gregorian') { sys = kind; showCalendar(); }
        });

        if (cfg.view === 'cards') showCards();
        else if (cfg.view === 'timeline') showTimeline();
        else if (cfg.view === 'kanban') showBoard();
        else if (cfg.view === 'gantt') showGantt();
        else if (cfg.view === 'tree') showTree();
        else showCalendar();
        root.pemsView = { reload: function () { if (cfg.view === 'calendar') showCalendar(); else if (cfg.view === 'kanban') showBoard(); else if (cfg.view === 'timeline') showTimeline(); else if (cfg.view === 'gantt') showGantt(); else if (cfg.view === 'tree') showTree(); else showCards(); } };
    }

    function scan(scope) { (scope || document).querySelectorAll('[data-pems-view]').forEach(init); }
    function boot() {
        scan(document);
        new MutationObserver(function (list) { list.forEach(function (m) { m.addedNodes.forEach(function (n) { if (n.nodeType === 1) { if (n.matches && n.matches('[data-pems-view]')) init(n); scan(n); } }); }); }).observe(document.body, { childList: true, subtree: true });
    }
    window.PemsViews = { init: init, scan: scan, jparts: function (d) { return jparts(d); }, jFirst: function (y, m) { return jFirst(y, m); } };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
