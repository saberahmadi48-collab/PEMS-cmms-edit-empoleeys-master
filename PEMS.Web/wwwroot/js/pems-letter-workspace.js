// PEMS Correspondence list: expanding search, compact density, row cursor + keyboard shortcuts, per-row archive.
// Selection, bulk actions and optional columns live in pems-letter-index.js; this file only drives them through their
// own controls (checkboxes / buttons), so every rule still lives on the server.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsLetterWorkspace = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var DENSITY_KEY = "pems.letter.compact";

    // Keys are matched on the physical key (event.code), so J/K/X/... work on a Persian keyboard layout too.
    // "?" is Shift+Slash. Returns null for keys the page does not use.
    function actionFor(e) {
        if (e.ctrlKey || e.metaKey || e.altKey) return null;
        switch (e.code) {
            case "Slash": return e.shiftKey ? "help" : "search";
            case "KeyC": return "compose";
            case "KeyJ": return "next";
            case "KeyK": return "prev";
            case "KeyX": return "select";
            case "KeyS": return "star";
            case "KeyE": return "archive";
            case "KeyD": return "density";
            case "Enter": return "open";
            case "Escape": return "escape";
            default: return null;
        }
    }

    // typing in a field, or any open dialog/menu, must never trigger a shortcut
    function isTypingTarget(el) {
        if (!el || !el.tagName) return false;
        var tag = el.tagName.toUpperCase();
        if (tag === "TEXTAREA" || tag === "SELECT") return true;
        if (tag === "INPUT") return !/^(checkbox|radio|button|submit)$/i.test(el.type || "");
        return !!el.isContentEditable;
    }

    function clampIndex(i, length) {
        if (length <= 0) return -1;
        return Math.max(0, Math.min(length - 1, i));
    }

    function read(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
    function write(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* not remembered, still works */ } }

    function init(doc) {
        var toolbar = doc.getElementById("lpToolbar");
        if (!toolbar) return null;

        var body = doc.body;
        var searchForm = doc.getElementById("lpSearchForm");
        var searchInput = doc.getElementById("lpSearchInput");
        var searchBtn = doc.getElementById("lpSearchBtn");

        // ---------------------------------------------------------------- search over the tabs
        function openSearch() {
            searchForm.hidden = false;
            toolbar.classList.add("lp-searching");
            searchBtn.setAttribute("aria-expanded", "true");
            searchInput.focus();
            searchInput.select();
        }
        function closeSearch(returnFocus) {
            searchForm.hidden = true;
            toolbar.classList.remove("lp-searching");
            searchBtn.setAttribute("aria-expanded", "false");
            if (returnFocus) searchBtn.focus();
        }
        searchBtn.addEventListener("click", function () { searchForm.hidden ? openSearch() : closeSearch(true); });
        doc.getElementById("lpSearchClose").addEventListener("click", function () { closeSearch(true); });
        searchInput.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { e.stopPropagation(); closeSearch(true); }
        });

        // ---------------------------------------------------------------- density
        var densityItem = doc.getElementById("lpDensity");
        function setCompact(on, persist) {
            body.classList.toggle("lp-compact", on);
            if (densityItem) densityItem.setAttribute("aria-checked", on ? "true" : "false");
            if (persist) write(DENSITY_KEY, on ? "1" : "0");
        }
        setCompact(read(DENSITY_KEY) === "1", false);
        if (densityItem) densityItem.addEventListener("click", function () { setCompact(!body.classList.contains("lp-compact"), true); });

        // ---------------------------------------------------------------- selection helpers (drive the real checkboxes)
        function rows() { return Array.prototype.slice.call(doc.querySelectorAll("table[data-grid-key='Letter'] tbody tr.lp-row:not(.ct-reply-row)")); }
        function checkbox(tr) { return tr && tr.querySelector(".letter-select"); }
        function fire(el) { el.dispatchEvent(new Event("change", { bubbles: true })); }

        function clearSelection() {
            rows().forEach(function (tr) { var c = checkbox(tr); if (c && c.checked) { c.checked = false; } });
            var all = doc.getElementById("selectAllLetters");
            if (all) { all.checked = false; fire(all); }
            else rows().forEach(function (tr) { var c = checkbox(tr); if (c) fire(c); });
        }
        var clearBtn = doc.getElementById("lpClearSelection");
        if (clearBtn) clearBtn.addEventListener("click", clearSelection);

        // archive a single row: select only it, then open the existing reason dialog (the server still requires the reason)
        function archiveRow(id) {
            var modalEl = doc.getElementById("letterArchiveModal");
            if (!modalEl || !window.bootstrap) return;
            rows().forEach(function (tr) {
                var c = checkbox(tr);
                if (!c) return;
                var want = String(tr.getAttribute("data-letter-id")) === String(id);
                if (c.checked !== want) { c.checked = want; fire(c); }
            });
            window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
        doc.addEventListener("click", function (e) {
            var b = e.target.closest ? e.target.closest("[data-row-archive]") : null;
            if (b) archiveRow(b.getAttribute("data-row-archive"));
        });

        // ---------------------------------------------------------------- keyboard cursor + shortcuts
        var cursor = -1;
        function moveCursor(to) {
            var list = rows();
            if (!list.length) return;
            cursor = clampIndex(to, list.length);
            list.forEach(function (tr, i) { tr.classList.toggle("lp-cursor", i === cursor); });
            list[cursor].scrollIntoView({ block: "nearest" });
        }
        function current() { var list = rows(); return cursor >= 0 ? list[cursor] : null; }

        doc.addEventListener("keydown", function (e) {
            var action = actionFor(e);
            if (!action) return;

            if (action === "escape") {
                if (!searchForm.hidden) { closeSearch(true); return; }
                if (doc.querySelector(".modal.show, .dropdown-menu.show, .offcanvas.show")) return;   // let Bootstrap close its own layer first
                clearSelection();
                return;
            }

            if (isTypingTarget(e.target) || doc.querySelector(".modal.show, .offcanvas.show")) return;
            // Enter on a focused link/button keeps its normal behaviour; it only opens the cursor row from the page body
            if (action === "open" && e.target !== doc.body) return;

            var tr = current();
            switch (action) {
                case "search": e.preventDefault(); openSearch(); break;
                case "compose": {
                    var c = doc.querySelector("[data-compose-url]");
                    if (c) { e.preventDefault(); window.location.href = c.getAttribute("data-compose-url"); }
                    break;
                }
                case "next": e.preventDefault(); moveCursor(cursor < 0 ? 0 : cursor + 1); break;
                case "prev": e.preventDefault(); moveCursor(cursor < 0 ? 0 : cursor - 1); break;
                case "select": if (tr) { e.preventDefault(); var cb = checkbox(tr); if (cb) { cb.checked = !cb.checked; fire(cb); } } break;
                case "star": if (tr) { var f = tr.querySelector("form[action*='ToggleStar']"); if (f) { e.preventDefault(); f.requestSubmit ? f.requestSubmit() : f.submit(); } } break;
                case "archive": if (tr) { var a = tr.querySelector("[data-row-archive]"); if (a) { e.preventDefault(); archiveRow(a.getAttribute("data-row-archive")); } } break;
                case "open": if (tr) { var link = tr.querySelector("a.lp-subject"); if (link) { e.preventDefault(); window.location.href = link.href; } } break;
                case "density": e.preventDefault(); setCompact(!body.classList.contains("lp-compact"), true); break;
                case "help": e.preventDefault(); if (window.bootstrap) window.bootstrap.Modal.getOrCreateInstance(doc.getElementById("lpShortcuts")).show(); break;
            }
        });

        // keep the keyboard cursor in sync with mouse clicks on a row
        doc.addEventListener("click", function (e) {
            var tr = e.target.closest ? e.target.closest("tr.lp-row") : null;
            if (tr) { var i = rows().indexOf(tr); if (i >= 0 && i !== cursor) { cursor = i; rows().forEach(function (r, k) { r.classList.toggle("lp-cursor", k === cursor); }); } }
        });

        return { openSearch: openSearch, setCompact: setCompact, moveCursor: moveCursor };
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", function () { init(document); });
        else init(document);
    }

    return { actionFor: actionFor, isTypingTarget: isTypingTarget, clampIndex: clampIndex, init: init };
});
