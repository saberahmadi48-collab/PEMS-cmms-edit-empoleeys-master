// PEMS Admin/PermissionMatrix: auto-save per toggle (cell, row, column), role/module search + filter,
// collapse/expand, per-cell and per-role save feedback, toasts with retry.
// The server (SetCells) re-validates the role, module and action and returns the saved flags; the UI is
// reconciled from that response, and a failed save puts every affected checkbox back. Without JavaScript the
// per-role form + Save button (SetRole) still works.
(function () {
    'use strict';

    var root = document.getElementById('pmRoot');
    if (!root) return;
    document.documentElement.classList.add('pm-js');

    var url = root.getAttribute('data-url');
    var totalModules = parseInt(root.getAttribute('data-total-modules'), 10) || 0;
    var i18n = {};
    try { i18n = JSON.parse(document.getElementById('pmI18n').textContent) || {}; } catch (e) { i18n = {}; }

    function t(key) {
        var s = i18n[key] || key;
        for (var i = 1; i < arguments.length; i++) s = s.split('{' + (i - 1) + '}').join(arguments[i]);
        return s;
    }
    function byId(id) { return document.getElementById(id); }
    function token() {
        var el = root.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    var roleEls = Array.prototype.slice.call(document.querySelectorAll('details.permission-role'));
    var chipByRole = {};
    document.querySelectorAll('#pmChips .pm-chip').forEach(function (c) { chipByRole[c.getAttribute('data-role')] = c; });
    roleEls.forEach(function (d) { d._rows = Array.prototype.slice.call(d.querySelectorAll('tbody tr[data-module]')); });

    function boxes(tr) { return Array.prototype.slice.call(tr.querySelectorAll('.pm-box')); }
    function boxOf(tr, action) { return tr.querySelector('.pm-box[data-action="' + action + '"]'); }
    function cellOf(tr, action) { var b = boxOf(tr, action); return b ? b.parentNode : null; }
    function roleOf(el) { return el.closest('details.permission-role'); }

    // ------------------------------------------------------------------ feedback
    function cellState(tr, action, state) {
        var cell = cellOf(tr, action);
        if (!cell) return;
        cell.classList.remove('is-saving', 'is-saved', 'is-error');
        if (!state) return;
        cell.classList.add('is-' + state);
        if (state === 'saved') setTimeout(function () { cell.classList.remove('is-saved'); }, 1300);
        if (state === 'error') setTimeout(function () { cell.classList.remove('is-error'); }, 6000);
    }

    function roleStatus(roleEl, state, text) {
        var el = roleEl.querySelector('.pm-role-status');
        if (!el) return;
        el.className = 'pm-role-status small' + (state ? ' is-' + state : '');
        el.textContent = text || '';
        if (state === 'saved') {
            clearTimeout(el._timer);
            el._timer = setTimeout(function () { if (el.classList.contains('is-saved')) { el.textContent = ''; el.className = 'pm-role-status small'; } }, 4000);
        }
    }

    var pending = 0;
    function setBusy(delta) {
        pending += delta;
        var el = byId('pmGlobalStatus');
        if (!el) return;
        el.className = 'pm-global-status small' + (pending > 0 ? ' is-saving' : '');
        el.textContent = pending > 0 ? t('saving') : '';
    }

    function toast(kind, message, retry) {
        var host = byId('pmToasts');
        if (!host) return;
        var el = document.createElement('div');
        el.className = 'pm-toast is-' + kind;
        el.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        var msg = document.createElement('span');
        msg.className = 'pm-toast-msg';
        msg.textContent = message;
        el.appendChild(msg);
        var close = function () { if (el.parentNode) el.parentNode.removeChild(el); };
        if (retry) {
            var rb = document.createElement('button');
            rb.type = 'button';
            rb.className = 'btn btn-sm btn-outline-primary';
            rb.textContent = t('retry');
            rb.addEventListener('click', function () { close(); retry(); });
            el.appendChild(rb);
        }
        var xb = document.createElement('button');
        xb.type = 'button';
        xb.className = 'btn-close';
        xb.setAttribute('aria-label', t('dismiss'));
        xb.addEventListener('click', close);
        el.appendChild(xb);
        host.appendChild(el);
        setTimeout(close, kind === 'error' ? 15000 : 3500);
    }

    // ------------------------------------------------------------------ aggregate checkboxes
    function syncRow(tr) {
        var all = tr.querySelector('.pm-row-all');
        if (!all) return;
        var bs = boxes(tr);
        var n = bs.filter(function (b) { return b.checked; }).length;
        all.checked = n === bs.length && bs.length > 0;
        all.indeterminate = n > 0 && n < bs.length;
    }

    function syncColumns(roleEl) {
        var visible = roleEl._rows.filter(function (tr) { return !tr.classList.contains('is-hidden'); });
        roleEl.querySelectorAll('.pm-col-all').forEach(function (head) {
            var action = head.getAttribute('data-action');
            var n = 0;
            visible.forEach(function (tr) { var b = boxOf(tr, action); if (b && b.checked) n++; });
            head.checked = visible.length > 0 && n === visible.length;
            head.indeterminate = n > 0 && n < visible.length;
        });
    }

    function setConfigured(roleEl, count) {
        roleEl.setAttribute('data-configured', count);
        var label = roleEl.querySelector('.pm-role-count');
        if (label) label.textContent = count === 0 ? t('noPermissions') : t('modulesFormat', count, totalModules);
        var chip = chipByRole[roleEl.getAttribute('data-role')];
        if (chip) {
            var c = chip.querySelector('.pm-chip-count');
            if (c) c.textContent = '(' + count + '/' + totalModules + ')';
            chip.classList.toggle('text-bg-secondary', count === 0);
            chip.classList.toggle('text-bg-light', count !== 0);
            chip.classList.toggle('border', count !== 0);
        }
    }

    // ------------------------------------------------------------------ saving
    var queue = Promise.resolve();

    function post(role, cells) {
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch', 'RequestVerificationToken': token() },
            body: JSON.stringify({ roleName: role, cells: cells })
        }).then(function (r) {
            var isJson = (r.headers.get('content-type') || '').indexOf('application/json') >= 0;
            if (r.redirected || !isJson) { var e = new Error('unexpected response'); e.code = null; throw e; }
            return r.json().then(function (j) {
                if (!r.ok) { var err = new Error('rejected'); err.code = j && j.error; throw err; }
                return j;
            });
        });
    }

    // changes: [{ tr, action, enabled, prev }]
    function submit(roleEl, changes) {
        var role = roleEl.getAttribute('data-role');
        changes.forEach(function (c) {
            boxOf(c.tr, c.action).checked = c.enabled;
            cellState(c.tr, c.action, 'saving');
            syncRow(c.tr);
        });
        syncColumns(roleEl);
        roleStatus(roleEl, 'saving', t('saving'));
        setBusy(1);

        var run = function () {
            return post(role, changes.map(function (c) {
                return { moduleName: c.tr.getAttribute('data-module'), action: c.action, enabled: c.enabled };
            })).then(function (data) {
                (data.modules || []).forEach(function (m) {
                    var tr = roleEl._rows.filter(function (r) { return r.getAttribute('data-module') === m.module; })[0];
                    if (!tr) return;
                    Object.keys(m.flags || {}).forEach(function (a) { var b = boxOf(tr, a); if (b) b.checked = !!m.flags[a]; });
                    tr.setAttribute('data-configured', Object.keys(m.flags || {}).some(function (a) { return m.flags[a]; }) ? '1' : '0');
                    syncRow(tr);
                });
                changes.forEach(function (c) { cellState(c.tr, c.action, 'saved'); });
                if (typeof data.configuredModules === 'number') setConfigured(roleEl, data.configuredModules);
                syncColumns(roleEl);
                roleStatus(roleEl, 'saved', t('saved'));
                if (changes.length > 1) toast('success', t('savedCount', changes.length));
            }, function (err) {
                changes.forEach(function (c) {
                    boxOf(c.tr, c.action).checked = c.prev;
                    cellState(c.tr, c.action, 'error');
                    syncRow(c.tr);
                });
                syncColumns(roleEl);
                var reason = (err && err.code && i18n[err.code]) || t('errorGeneric');
                roleStatus(roleEl, 'error', t('failed'));
                toast('error', t('failed') + ' ' + reason, function () { submit(roleEl, changes); });
            }).then(function () { setBusy(-1); });
        };
        queue = queue.then(run, run);
    }

    function changesFor(trs, action, enabled) {
        var out = [];
        trs.forEach(function (tr) {
            var b = boxOf(tr, action);
            if (b && b.checked !== enabled && !b.parentNode.classList.contains('is-saving')) out.push({ tr: tr, action: action, enabled: enabled, prev: b.checked });
        });
        return out;
    }

    // ------------------------------------------------------------------ events
    root.addEventListener('change', function (e) {
        var el = e.target;
        if (!(el instanceof HTMLInputElement)) return;
        var roleEl = roleOf(el);
        if (!roleEl) return;
        var tr = el.closest('tr[data-module]');

        if (el.classList.contains('pm-box') && tr) {
            // the checkbox already flipped; remember the value to go back to
            submit(roleEl, [{ tr: tr, action: el.getAttribute('data-action'), enabled: el.checked, prev: !el.checked }]);
        } else if (el.classList.contains('pm-row-all') && tr) {
            var wanted = el.checked;
            var rowChanges = [];
            boxes(tr).forEach(function (b) {
                if (b.checked !== wanted && !b.parentNode.classList.contains('is-saving'))
                    rowChanges.push({ tr: tr, action: b.getAttribute('data-action'), enabled: wanted, prev: b.checked });
            });
            if (rowChanges.length) submit(roleEl, rowChanges); else syncRow(tr);
        } else if (el.classList.contains('pm-col-all')) {
            var visible = roleEl._rows.filter(function (r) { return !r.classList.contains('is-hidden'); });
            var colChanges = changesFor(visible, el.getAttribute('data-action'), el.checked);
            if (colChanges.length) submit(roleEl, colChanges); else syncColumns(roleEl);
        }
    });

    // ------------------------------------------------------------------ search + filter
    function highlight(span, query) {
        var text = span.getAttribute('data-text');
        if (text === null) { text = span.textContent; span.setAttribute('data-text', text); }
        while (span.firstChild) span.removeChild(span.firstChild);
        var idx = query ? text.toLowerCase().indexOf(query) : -1;
        if (idx < 0) { span.appendChild(document.createTextNode(text)); return; }
        span.appendChild(document.createTextNode(text.slice(0, idx)));
        var mark = document.createElement('mark');
        mark.className = 'pm-hit';
        mark.textContent = text.slice(idx, idx + query.length);
        span.appendChild(mark);
        span.appendChild(document.createTextNode(text.slice(idx + query.length)));
    }

    function applyFilters() {
        var rq = (byId('pmRoleSearch').value || '').trim().toLowerCase();
        var mq = (byId('pmModuleSearch').value || '').trim().toLowerCase();
        var status = byId('pmStatusFilter').value;
        var filtersActive = !!mq || status !== 'all';
        var shown = 0;

        roleEls.forEach(function (d) {
            var name = d.getAttribute('data-role');
            var visibleRows = 0;
            d._rows.forEach(function (tr) {
                var m = tr.getAttribute('data-module');
                var configured = tr.getAttribute('data-configured') === '1';
                var ok = (!mq || m.toLowerCase().indexOf(mq) >= 0)
                    && (status === 'all' || (status === 'configured') === configured);
                tr.classList.toggle('is-hidden', !ok);
                var label = tr.querySelector('.pm-module-name');
                if (label) highlight(label, ok ? mq : '');
                if (ok) visibleRows++;
            });
            var show = (!rq || name.toLowerCase().indexOf(rq) >= 0) && (!filtersActive || visibleRows > 0);
            d.classList.toggle('is-hidden', !show);
            if (chipByRole[name]) chipByRole[name].classList.toggle('is-hidden', !show);
            if (show) { shown++; if (mq) d.open = true; syncColumns(d); }
        });

        byId('pmRolesShown').textContent = t('rolesShown', shown, roleEls.length);
        byId('pmNoRole').classList.toggle('d-none', shown !== 0);
    }

    var timer = null;
    function scheduleFilter() { clearTimeout(timer); timer = setTimeout(applyFilters, 120); }
    byId('pmRoleSearch').addEventListener('input', scheduleFilter);
    byId('pmModuleSearch').addEventListener('input', scheduleFilter);
    byId('pmStatusFilter').addEventListener('change', applyFilters);

    function setAllOpen(open) {
        roleEls.forEach(function (d) { if (!d.classList.contains('is-hidden')) d.open = open; });
    }
    byId('pmExpandAll').addEventListener('click', function () { setAllOpen(true); });
    byId('pmCollapseAll').addEventListener('click', function () { setAllOpen(false); });

    // ------------------------------------------------------------------ deep links (#role-Name)
    function openFromHash() {
        var id = decodeURIComponent((location.hash || '').replace(/^#/, ''));
        if (!id) return;
        var target = byId(id);
        if (target && target.tagName === 'DETAILS') {
            target.classList.remove('is-hidden');
            target.open = true;
            target.scrollIntoView({ block: 'start' });
        }
    }
    window.addEventListener('hashchange', openFromHash);
    document.querySelectorAll('#pmChips .pm-chip').forEach(function (chip) {
        chip.addEventListener('click', function () { setTimeout(openFromHash, 0); });
    });

    // initial state
    roleEls.forEach(function (d) {
        d._rows.forEach(syncRow);
        syncColumns(d);
    });
    applyFilters();
    openFromHash();
})();
