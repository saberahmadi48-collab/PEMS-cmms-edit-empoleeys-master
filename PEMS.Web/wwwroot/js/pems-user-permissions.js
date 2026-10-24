// PEMS Admin/Users/Details > Access & Authorization > Effective: auto-saving permission toggles and batch controls.
// A user's effective permissions come from the grants of their roles, so every change is written to the role(s)
// by SetPermissionAccess (the server re-validates which rows are editable). The UI never assumes success: each row
// is updated from the state the server returns, and a failed save puts the switch back where it was.
(function () {
    'use strict';

    var root = document.getElementById('permAdmin');
    if (!root) return;

    var editable = root.getAttribute('data-editable') === '1';
    var url = root.getAttribute('data-url');
    var bodyAllowed = document.querySelector('#permTableAllowed tbody');
    var bodyDenied = document.querySelector('#permTableDenied tbody');
    var alertBox = document.getElementById('permAlert');
    var busyEl = document.getElementById('permBusy');
    var i18n = {};
    try { i18n = JSON.parse(document.getElementById('permAdminI18n').textContent) || {}; } catch (e) { i18n = {}; }

    function t(key) {
        var s = i18n[key] || key;
        for (var i = 1; i < arguments.length; i++) s = s.split('{' + (i - 1) + '}').join(arguments[i]);
        return s;
    }

    function token() {
        var el = root.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function allRows() { return Array.prototype.slice.call(root.querySelectorAll('tr[data-permission]')); }
    function rowByPermission(name) {
        return allRows().filter(function (tr) { return tr.getAttribute('data-permission') === name; })[0] || null;
    }
    function isEditable(tr) { return editable && tr.getAttribute('data-locked') !== '1'; }
    function isAllowed(tr) { return tr.getAttribute('data-allowed') === '1'; }
    function targetRole() {
        var sel = document.getElementById('permTargetRole');
        return sel ? sel.value : null;
    }

    // ------------------------------------------------------------------ feedback
    var alertTimer = null;
    function showAlert(kind, text) {
        if (!alertBox) return;
        clearTimeout(alertTimer);
        alertBox.className = 'alert alert-' + kind + ' py-2 small';
        alertBox.textContent = text;
        if (kind === 'success') alertTimer = setTimeout(function () { alertBox.className = 'alert d-none py-2 small'; }, 4000);
    }

    function rowState(tr, kind, text) {
        var el = tr.querySelector('.perm-row-state');
        if (!el) return;
        el.className = 'perm-row-state small';
        el.textContent = '';
        var icon = function (cls) {
            var i = document.createElement('span');
            i.className = cls;
            i.setAttribute('aria-hidden', 'true');
            el.appendChild(i);
            el.appendChild(document.createTextNode(' '));
        };
        if (kind === 'saving') {
            icon('spinner-border spinner-border-sm text-secondary');
            el.appendChild(document.createTextNode(t('saving')));
        } else if (kind === 'ok') {
            el.classList.add('text-success');
            icon('bi bi-check2');
            el.appendChild(document.createTextNode(text || t('rowSaved')));
            setTimeout(function () { if (el.classList.contains('text-success')) el.textContent = ''; }, 2500);
        } else if (kind === 'error') {
            el.classList.add('text-danger');
            el.textContent = text || '';
        }
    }

    var pending = 0;
    function setBusy(delta) {
        pending += delta;
        if (busyEl) busyEl.classList.toggle('d-none', pending <= 0);
    }

    // ------------------------------------------------------------------ rendering a row's state
    function sortKey(tr) { return tr.getAttribute('data-resource') + '\u0001' + tr.getAttribute('data-action'); }

    function placeRow(tr, allowed) {
        var body = allowed ? bodyAllowed : bodyDenied;
        if (!body || tr.parentNode === body) return;
        var key = sortKey(tr);
        var before = null;
        for (var i = 0; i < body.children.length; i++) {
            if (sortKey(body.children[i]) > key) { before = body.children[i]; break; }
        }
        body.insertBefore(tr, before);
    }

    function renderRow(tr, allowed) {
        tr.setAttribute('data-allowed', allowed ? '1' : '0');
        tr.classList.toggle('text-body-secondary', !allowed);
        var toggle = tr.querySelector('.perm-toggle');
        if (toggle) toggle.checked = allowed;
        var label = tr.querySelector('.perm-status-label');
        if (label) {
            label.classList.toggle('active', allowed);
            label.classList.toggle('inactive', !allowed);
            var icon = label.querySelector('i');
            if (icon) icon.className = 'bi ' + (allowed ? 'bi-check2-circle' : 'bi-dash-circle');
            var text = label.querySelector('.perm-status-text');
            if (text) text.textContent = allowed ? t('allowed') : t('notGranted');
        }
        placeRow(tr, allowed);
    }

    function setCounts(allowedCount, totalCount) {
        var set = function (id, v) { var el = document.getElementById(id); if (el) el.textContent = v; };
        set('permAllowedCount', allowedCount);
        set('permTotalCount', totalCount);
        set('permInfoAllowed', allowedCount);
        set('permTabBadge', allowedCount);
        set('permNotGrantedCount', totalCount - allowedCount);
    }

    // ------------------------------------------------------------------ selection
    function selectedRows() {
        return allRows().filter(function (tr) {
            var cb = tr.querySelector('.perm-select');
            return cb && cb.checked && isEditable(tr);
        });
    }

    function updateSelectionUi() {
        if (!editable) return;
        var sel = selectedRows();
        var needEnable = sel.filter(function (tr) { return !isAllowed(tr); }).length;
        var needDisable = sel.filter(isAllowed).length;
        var en = document.getElementById('permEnableSelected');
        var dis = document.getElementById('permDisableSelected');
        if (en) en.disabled = needEnable === 0;
        if (dis) dis.disabled = needDisable === 0;
        var counter = document.getElementById('permSelectedCount');
        if (counter) counter.textContent = sel.length ? t('selectedCount', sel.length) : '';

        ['permTableAllowed', 'permTableDenied'].forEach(function (id) {
            var table = document.getElementById(id);
            if (!table) return;
            var header = table.querySelector('.perm-select-all');
            if (!header) return;
            var boxes = Array.prototype.slice.call(table.querySelectorAll('tbody .perm-select')).filter(function (cb) { return !cb.disabled; });
            var checked = boxes.filter(function (cb) { return cb.checked; }).length;
            header.checked = boxes.length > 0 && checked === boxes.length;
            header.indeterminate = checked > 0 && checked < boxes.length;
        });
    }

    function setAllSelected(value, scope) {
        (scope || root).querySelectorAll('tbody .perm-select').forEach(function (cb) {
            if (!cb.disabled) cb.checked = value;
        });
        updateSelectionUi();
    }

    // ------------------------------------------------------------------ saving
    var queue = Promise.resolve();

    function post(changes) {
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'fetch',
                'RequestVerificationToken': token()
            },
            body: JSON.stringify({ targetRole: targetRole(), changes: changes })
        }).then(function (r) {
            var json = (r.headers.get('content-type') || '').indexOf('application/json') >= 0;
            if (!r.ok || r.redirected || !json) throw new Error('http ' + r.status);
            return r.json();
        });
    }

    // Saves one request; resolves with the server response or null when it failed (rows are put back by the caller).
    function save(rows, enable) {
        var changes = rows.map(function (tr) { return { permissionName: tr.getAttribute('data-permission'), enable: enable }; });
        rows.forEach(function (tr) { rowState(tr, 'saving'); });
        setBusy(1);

        var run = function () {
            return post(changes).then(function (data) {
                applyResponse(rows, data);
                return data;
            }, function () {
                rows.forEach(function (tr) {
                    renderRow(tr, isAllowed(tr)); // put the switch back to the last known server state
                    rowState(tr, 'error', t('failed'));
                });
                showAlert('danger', t('failed'));
                return null;
            }).then(function (data) { setBusy(-1); updateSelectionUi(); return data; });
        };
        queue = queue.then(run, run);
        return queue;
    }

    function applyResponse(rows, data) {
        var stateByName = {};
        (data.states || []).forEach(function (s) { stateByName[s.permission] = s.allowed; });
        var skipped = 0;
        var lastReason = null;

        (data.items || []).forEach(function (item) {
            var tr = rowByPermission(item.permission);
            if (!tr) return;
            if (Object.prototype.hasOwnProperty.call(stateByName, item.permission)) renderRow(tr, stateByName[item.permission]);
            if (item.applied) {
                rowState(tr, 'ok');
            } else {
                skipped++;
                var reason = item.reason === 'NoChange' ? t('NoChangeReason') : (i18n[item.reason] || item.reason || t('failed'));
                lastReason = reason;
                rowState(tr, item.reason === 'NoChange' ? 'ok' : 'error', item.reason === 'NoChange' ? '' : reason);
            }
        });

        if (typeof data.allowedCount === 'number') setCounts(data.allowedCount, data.totalCount);

        // rows the response did not mention keep their previous state
        rows.forEach(function (tr) { renderRow(tr, isAllowed(tr)); });

        var changed = (data.applied || 0);
        if (changed > 0 && skipped === 0) showAlert('success', t('saved', changed));
        else if (changed > 0) showAlert('warning', t('saved', changed) + ' ' + t('someSkipped', skipped));
        else showAlert('warning', lastReason || t('noChange'));
    }

    // ------------------------------------------------------------------ events
    root.addEventListener('change', function (e) {
        var el = e.target;
        if (!(el instanceof HTMLInputElement)) return;
        var tr = el.closest('tr[data-permission]');

        if (el.classList.contains('perm-toggle') && tr) {
            if (!isEditable(tr)) { el.checked = isAllowed(tr); return; }
            save([tr], el.checked);
        } else if (el.classList.contains('perm-select')) {
            updateSelectionUi();
        } else if (el.classList.contains('perm-select-all')) {
            setAllSelected(el.checked, el.closest('table'));
        }
    });

    function batch(enable) {
        var rows = selectedRows().filter(function (tr) { return isAllowed(tr) !== enable; });
        if (!rows.length) { showAlert('warning', t('nothingSelected')); return; }
        if (!window.confirm(t('confirmBatch', rows.length))) return;
        save(rows, enable).then(function (data) {
            if (data) rows.forEach(function (tr) { var cb = tr.querySelector('.perm-select'); if (cb) cb.checked = false; });
            updateSelectionUi();
        });
    }

    var byId = function (id) { return document.getElementById(id); };
    if (byId('permSelectAll')) byId('permSelectAll').addEventListener('click', function () { setAllSelected(true); });
    if (byId('permDeselectAll')) byId('permDeselectAll').addEventListener('click', function () { setAllSelected(false); });
    if (byId('permEnableSelected')) byId('permEnableSelected').addEventListener('click', function () { batch(true); });
    if (byId('permDisableSelected')) byId('permDisableSelected').addEventListener('click', function () { batch(false); });

    updateSelectionUi();
})();
