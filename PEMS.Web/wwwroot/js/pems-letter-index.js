// PEMS Correspondence list: row selection + bulk bar, optional-column visibility (remembered per browser) and the
// archive-reason dialog. Server-side rules (Draft/Rejected only, owner-or-admin, Delete permission, audit) are enforced by
// the service; this script only collects the selection and keeps the UI honest (button state, counts, required reason).
// Pure helpers are exported for Node tests; the DOM part only runs in a browser.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsLetterIndex = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var STORAGE_KEY = "pems.letter.index.hiddenColumns";
    var HIDDEN_CLASS = "pems-col-hidden";

    function selectionState(total, checked) {
        return { none: checked === 0, all: total > 0 && checked === total, some: checked > 0 && checked < total };
    }

    function format(template, n) {
        return String(template || "").split("{0}").join(String(n));
    }

    // only keys that are really optional columns are accepted from storage (a stale/tampered value cannot hide "subject")
    function parseHidden(raw, optionalKeys) {
        var list;
        try { list = JSON.parse(raw); } catch (e) { return null; }
        if (!Array.isArray(list)) return null;
        return list.filter(function (k) { return optionalKeys.indexOf(k) !== -1; });
    }

    function setHidden(list, key, hidden) {
        var without = list.filter(function (k) { return k !== key; });
        return hidden ? without.concat([key]) : without;
    }

    function archiveReasonValid(reason) {
        return String(reason == null ? "" : reason).trim().length > 0;
    }

    function readStore() { try { return window.localStorage.getItem(STORAGE_KEY); } catch (e) { return null; } }
    function writeStore(value) { try { window.localStorage.setItem(STORAGE_KEY, value); } catch (e) { /* storage unavailable: the choice just isn't remembered */ } }

    function applyColumns(table, hiddenKeys) {
        var headers = Array.prototype.slice.call(table.querySelectorAll("thead th"));
        headers.forEach(function (th, index) {
            var hide = hiddenKeys.indexOf(th.getAttribute("data-col-key")) !== -1;
            th.classList.toggle(HIDDEN_CLASS, hide);
            Array.prototype.forEach.call(table.querySelectorAll("tbody tr"), function (tr) {
                var cell = tr.children[index];
                if (cell) cell.classList.toggle(HIDDEN_CLASS, hide);
            });
        });
    }

    function init(doc, cfg) {
        cfg = cfg || {};
        var table = doc.querySelector('table[data-grid-key="Letter"]');
        var form = doc.getElementById("letterBulkForm");
        if (!table) return null;

        // ---- optional columns
        var toggles = Array.prototype.slice.call(doc.querySelectorAll("[data-col-toggle]"));
        var optional = toggles.map(function (t) { return t.getAttribute("data-col-toggle"); });
        var defaults = toggles.filter(function (t) { return t.getAttribute("data-default-hidden") === "true"; })
            .map(function (t) { return t.getAttribute("data-col-toggle"); });
        var stored = readStore();
        var hiddenCols = (stored != null && parseHidden(stored, optional)) || defaults;

        function syncToggles() {
            toggles.forEach(function (t) { t.checked = hiddenCols.indexOf(t.getAttribute("data-col-toggle")) === -1; });
            applyColumns(table, hiddenCols);
        }
        toggles.forEach(function (t) {
            t.addEventListener("change", function () {
                hiddenCols = setHidden(hiddenCols, t.getAttribute("data-col-toggle"), !t.checked);
                writeStore(JSON.stringify(hiddenCols));
                applyColumns(table, hiddenCols);
            });
        });
        syncToggles();

        var resetBtn = doc.getElementById("letterColumnsReset");
        if (resetBtn) resetBtn.addEventListener("click", function () {
            hiddenCols = defaults.slice();
            writeStore(JSON.stringify(hiddenCols));
            syncToggles();
        });

        // ---- selection + bulk bar
        var selectAll = doc.getElementById("selectAllLetters");
        var checks = Array.prototype.slice.call(doc.querySelectorAll(".letter-select"));
        var counter = doc.getElementById("letterBulkCount");
        var bulkButtons = Array.prototype.slice.call(doc.querySelectorAll("[data-bulk-op]"));

        function checkedIds() { return checks.filter(function (c) { return c.checked; }).map(function (c) { return c.value; }); }

        function refresh() {
            var n = checkedIds().length;
            var state = selectionState(checks.length, n);
            if (selectAll) { selectAll.checked = state.all; selectAll.indeterminate = state.some; }
            bulkButtons.forEach(function (b) { b.disabled = state.none; });
            if (counter) counter.textContent = state.none ? "" : format(cfg.selected || counter.getAttribute("data-template"), n);
            var bar = doc.getElementById("letterBulkBar");
            if (bar) bar.classList.toggle("pems-bulk-active", !state.none);
        }

        if (selectAll) selectAll.addEventListener("change", function () {
            checks.forEach(function (c) { c.checked = selectAll.checked; });
            refresh();
        });
        checks.forEach(function (c) { c.addEventListener("change", refresh); });
        refresh();

        if (form) {
            var idHost = doc.getElementById("letterBulkIds");
            var reason = doc.getElementById("letterArchiveReason");
            var reasonError = doc.getElementById("letterArchiveReasonError");

            form.addEventListener("submit", function (e) {
                var ids = checkedIds();
                var op = e.submitter ? e.submitter.value : "";
                if (ids.length === 0) { e.preventDefault(); return; }

                if (op === "archive" && !archiveReasonValid(reason && reason.value)) {
                    e.preventDefault();
                    if (reason) { reason.setAttribute("aria-invalid", "true"); reason.focus(); }
                    if (reasonError) reasonError.hidden = false;
                    return;
                }

                var confirmText = e.submitter && e.submitter.getAttribute("data-confirm");
                if (confirmText && !window.confirm(confirmText)) { e.preventDefault(); return; }

                idHost.textContent = "";
                ids.forEach(function (id) {
                    var input = doc.createElement("input");
                    input.type = "hidden"; input.name = "ids"; input.value = id;
                    idHost.appendChild(input);
                });
            });

            if (reason) reason.addEventListener("input", function () {
                if (archiveReasonValid(reason.value)) {
                    reason.removeAttribute("aria-invalid");
                    if (reasonError) reasonError.hidden = true;
                }
            });
        }

        return { refresh: refresh, selected: checkedIds };
    }

    if (typeof document !== "undefined" && document.currentScript) {
        var ds = document.currentScript.dataset || {};
        var start = function () { init(document, { selected: ds.selected }); };
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start); else start();
    }

    return { selectionState: selectionState, format: format, parseHidden: parseHidden, setHidden: setHidden, archiveReasonValid: archiveReasonValid, init: init };
});
