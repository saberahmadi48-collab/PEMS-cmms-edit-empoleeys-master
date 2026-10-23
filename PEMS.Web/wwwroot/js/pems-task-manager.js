// PEMS Task Manager: board drag-and-drop (status transitions), backlog drag reorder, bulk selection,
// Ctrl+Enter submit, unsaved-changes guard, confirm prompts, create-another auto-open.
// Every action is also available without drag (per-card "move to" form, rank up/down buttons); the server
// re-validates permissions and transitions for each request.
(function () {
    'use strict';

    function token() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function post(url, data) {
        var body = new URLSearchParams();
        Object.keys(data).forEach(function (k) {
            var v = data[k];
            if (Array.isArray(v)) v.forEach(function (x) { body.append(k, x); });
            else if (v !== null && v !== undefined) body.append(k, v);
        });
        body.append('__RequestVerificationToken', token());
        return fetch(url, {
            method: 'POST',
            headers: { 'X-Requested-With': 'fetch', 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body,
            credentials: 'same-origin'
        }).then(function (r) {
            return r.json().catch(function () { return { ok: false }; }).then(function (j) { j.status = r.status; return j; });
        });
    }

    // ---------------------------------------------------------------- confirm prompts
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-tm-confirm]');
        if (btn && !window.confirm(btn.getAttribute('data-tm-confirm'))) e.preventDefault();
    });

    // ---------------------------------------------------------------- Ctrl+Enter + dirty guard
    document.querySelectorAll('form[data-tm-ctrl-enter]').forEach(function (form) {
        form.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                e.preventDefault();
                if (form.reportValidity()) form.requestSubmit ? form.requestSubmit() : form.submit();
            }
        });
    });

    var dirty = new Set();
    document.querySelectorAll('form[data-tm-dirty-guard]').forEach(function (form) {
        form.addEventListener('input', function () { dirty.add(form); });
        form.addEventListener('submit', function () { dirty.delete(form); });
        form.addEventListener('reset', function () { dirty.delete(form); });
    });
    window.addEventListener('beforeunload', function (e) {
        if (dirty.size > 0) { e.preventDefault(); e.returnValue = ''; }
    });
    var createModal = document.getElementById('tmCreateModal');
    if (createModal && window.bootstrap) {
        createModal.addEventListener('shown.bs.modal', function () {
            var t = createModal.querySelector('input[name="Title"]');
            if (t) t.focus();
        });
        createModal.addEventListener('hidden.bs.modal', function () {
            var form = createModal.querySelector('form');
            if (form) { form.reset(); dirty.delete(form); }
        });
        if (createModal.getAttribute('data-tm-autoopen') === '1') {
            window.bootstrap.Modal.getOrCreateInstance(createModal).show();
            var another = createModal.querySelector('#tmc-another');
            if (another) another.checked = true;
        }
    }

    // ---------------------------------------------------------------- bulk selection
    var bulkBar = document.querySelector('[data-tm-bulkbar]');
    function refreshBulk() {
        if (!bulkBar) return;
        var n = document.querySelectorAll('[data-tm-bulk-item]:checked').length;
        bulkBar.hidden = n === 0;
        var hint = document.querySelector('[data-tm-bulk-hint]');
        if (hint) hint.hidden = n > 0;
        var c = bulkBar.querySelector('[data-tm-bulk-count]');
        if (c) c.textContent = n;
    }
    document.addEventListener('change', function (e) {
        if (e.target.matches('[data-tm-bulk-all]')) {
            var table = e.target.closest('table');
            table.querySelectorAll('[data-tm-bulk-item]').forEach(function (cb) { cb.checked = e.target.checked; });
        }
        if (e.target.matches('[data-tm-bulk-item], [data-tm-bulk-all]')) refreshBulk();
    });
    if (bulkBar) {
        var actionSel = bulkBar.querySelector('[data-tm-bulk-action]');
        var syncValue = function () {
            bulkBar.querySelectorAll('[data-tm-bulk-value]').forEach(function (sel) {
                var on = sel.getAttribute('data-tm-bulk-value') === actionSel.value;
                sel.hidden = !on;
                sel.disabled = !on;
            });
        };
        actionSel.addEventListener('change', syncValue);
        syncValue();
        refreshBulk();
    }

    // ---------------------------------------------------------------- board DnD (transitions)
    var board = document.querySelector('[data-tm-board]');
    if (board) {
        var transitions = {};
        try { transitions = JSON.parse(board.getAttribute('data-tm-transitions') || '{}'); } catch (err) { transitions = {}; }
        var url = board.getAttribute('data-tm-transition-url');
        var dragged = null;

        var allowed = function (from, to) {
            return from !== to && (transitions[from] || []).map(String).indexOf(String(to)) >= 0;
        };
        var countUpdate = function () {
            board.querySelectorAll('[data-tm-status]').forEach(function (col) {
                var c = col.querySelector('[data-tm-col-count]');
                if (c) c.textContent = col.querySelectorAll('[data-tm-card]').length;
            });
        };

        board.addEventListener('dragstart', function (e) {
            var card = e.target.closest('[data-tm-card][draggable="true"]');
            if (!card) return;
            dragged = card;
            card.classList.add('tm-dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', card.getAttribute('data-tm-id'));
        });
        board.addEventListener('dragend', function () {
            if (dragged) dragged.classList.remove('tm-dragging');
            dragged = null;
            board.querySelectorAll('.tm-drop-ok, .tm-drop-no').forEach(function (z) { z.classList.remove('tm-drop-ok', 'tm-drop-no'); });
        });
        board.addEventListener('dragover', function (e) {
            var zone = e.target.closest('[data-tm-dropzone]');
            if (!zone || !dragged) return;
            var to = zone.closest('[data-tm-status]').getAttribute('data-tm-status');
            var ok = allowed(dragged.getAttribute('data-tm-from'), to);
            zone.classList.toggle('tm-drop-ok', ok);
            zone.classList.toggle('tm-drop-no', !ok);
            if (ok) { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; }
        });
        board.addEventListener('dragleave', function (e) {
            var zone = e.target.closest('[data-tm-dropzone]');
            if (zone && !zone.contains(e.relatedTarget)) zone.classList.remove('tm-drop-ok', 'tm-drop-no');
        });
        board.addEventListener('drop', function (e) {
            var zone = e.target.closest('[data-tm-dropzone]');
            if (!zone || !dragged) return;
            e.preventDefault();
            var card = dragged;
            var from = card.getAttribute('data-tm-from');
            var to = zone.closest('[data-tm-status]').getAttribute('data-tm-status');
            zone.classList.remove('tm-drop-ok', 'tm-drop-no');
            if (!allowed(from, to)) { window.alert(board.getAttribute('data-tm-msg-invalid')); return; }
            var origin = card.parentElement;
            zone.appendChild(card);
            countUpdate();
            post(url, { id: card.getAttribute('data-tm-id'), statusId: to, rowVersion: card.getAttribute('data-tm-rv') })
                .then(function (r) {
                    if (r.ok) { window.location.reload(); return; }   // تازه‌سازی: RowVersion و گذارهای مجاز جدید
                    origin.appendChild(card);
                    countUpdate();
                    window.alert(r.error || board.getAttribute('data-tm-msg-failed'));
                })
                .catch(function () {
                    origin.appendChild(card);
                    countUpdate();
                    window.alert(board.getAttribute('data-tm-msg-failed'));
                });
        });
    }

    // ---------------------------------------------------------------- backlog drag reorder (Lead only: rows are draggable only then)
    document.querySelectorAll('tbody[data-tm-bucket]').forEach(function (tbody) {
        var row = null;
        tbody.addEventListener('dragstart', function (e) {
            var tr = e.target.closest('tr[draggable="true"]');
            if (!tr) return;
            row = tr;
            tr.classList.add('tm-dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', tr.getAttribute('data-tm-id'));
        });
        tbody.addEventListener('dragover', function (e) {
            if (!row) return;
            var over = e.target.closest('tr[data-tm-id]');
            if (!over || over === row || over.parentElement !== tbody) return;
            e.preventDefault();
            var rect = over.getBoundingClientRect();
            tbody.insertBefore(row, (e.clientY - rect.top) > rect.height / 2 ? over.nextSibling : over);
        });
        tbody.addEventListener('dragend', function () {
            if (!row) return;
            row.classList.remove('tm-dragging');
            row = null;
            var parts = (tbody.getAttribute('data-tm-bucket') || '').split(':');
            var ids = Array.prototype.map.call(tbody.querySelectorAll('tr[data-tm-id]'), function (tr) { return tr.getAttribute('data-tm-id'); });
            var holder = document.querySelector('[data-tm-reorder-url]');
            if (!holder) return;
            post(holder.getAttribute('data-tm-reorder-url'), { workSpaceId: parts[0], sprintId: parts[1] || null, ids: ids })
                .then(function (r) { if (!r.ok) window.alert(r.error || ''); window.location.reload(); })
                .catch(function () { window.location.reload(); });
        });
    });
})();
