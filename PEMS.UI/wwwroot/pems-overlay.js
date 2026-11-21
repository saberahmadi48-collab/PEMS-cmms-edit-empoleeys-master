// PEMS.UI overlays and navigation (SCRUM-338). Vanilla, offline, idempotent (dataset.pemsInit), no inline handlers.
//   [data-pems-open="#id"]      opens that <dialog>; [data-pems-row-key] is copied to dialog.dataset.rowKey, to every
//                               [data-pems-row-key-target] input value and sent in the "pems:dialog-open" event (detail.rowKey)
//   [data-pems-close]           closes the dialog it is in;  a click on the backdrop and Escape close it (native <dialog>)
//   [data-pems-tabs]            tablist/tabpanel with arrow keys, Home and End
//   [data-pems-menu]            <details> menu: closes on Escape and on an outside click
//   [data-pems-copy="text"]     copies text to the clipboard;  [data-pems-dismiss] removes its alert
(function () {
    'use strict';

    function openDialog(dialog, opener) {
        var key = opener ? opener.getAttribute('data-pems-row-key') : null;
        if (key !== null) {
            dialog.dataset.rowKey = key;
            dialog.querySelectorAll('[data-pems-row-key-target]').forEach(function (t) { t.value = key; });
        }
        if (typeof dialog.showModal === 'function' && !dialog.open) dialog.showModal();
        dialog.dispatchEvent(new CustomEvent('pems:dialog-open', { bubbles: true, detail: { rowKey: key } }));
    }

    function initDialog(dialog) {
        if (dialog.dataset.pemsInit) return;
        dialog.dataset.pemsInit = '1';
        // a click on the backdrop (the dialog element itself, outside the panel) closes it
        dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close(); });
    }

    function initTabs(root) {
        if (root.dataset.pemsInit) return;
        root.dataset.pemsInit = '1';
        var tabs = Array.prototype.slice.call(root.querySelectorAll('[role="tab"]'));
        function select(tab, focus) {
            tabs.forEach(function (t) {
                var on = t === tab;
                t.setAttribute('aria-selected', on ? 'true' : 'false');
                t.tabIndex = on ? 0 : -1;
                var panel = document.getElementById(t.getAttribute('aria-controls'));
                if (panel) panel.hidden = !on;
            });
            if (focus) tab.focus();
        }
        tabs.forEach(function (tab, i) {
            tab.addEventListener('click', function () { select(tab, false); });
            tab.addEventListener('keydown', function (e) {
                var rtl = getComputedStyle(root).direction === 'rtl';
                var next = null;
                if (e.key === 'ArrowRight') next = tabs[(i + (rtl ? -1 : 1) + tabs.length) % tabs.length];
                else if (e.key === 'ArrowLeft') next = tabs[(i + (rtl ? 1 : -1) + tabs.length) % tabs.length];
                else if (e.key === 'Home') next = tabs[0];
                else if (e.key === 'End') next = tabs[tabs.length - 1];
                if (next) { e.preventDefault(); select(next, true); }
            });
        });
    }

    function initMenu(menu) {
        if (menu.dataset.pemsInit) return;
        menu.dataset.pemsInit = '1';
        menu.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && menu.open) { menu.open = false; var s = menu.querySelector('summary'); if (s) s.focus(); }
        });
    }

    function init(scope) {
        (scope || document).querySelectorAll('dialog[data-pems-dialog]').forEach(initDialog);
        (scope || document).querySelectorAll('[data-pems-tabs]').forEach(initTabs);
        (scope || document).querySelectorAll('[data-pems-menu]').forEach(initMenu);
    }

    // one delegated listener for the whole document (bound once)
    if (!document.documentElement.dataset.pemsOverlayBound) {
        document.documentElement.dataset.pemsOverlayBound = '1';
        document.addEventListener('click', function (e) {
            var opener = e.target.closest('[data-pems-open]');
            if (opener) {
                var dialog = document.querySelector(opener.getAttribute('data-pems-open'));
                if (dialog) { initDialog(dialog); openDialog(dialog, opener); }
                return;
            }
            var closer = e.target.closest('[data-pems-close]');
            if (closer) { var d = closer.closest('dialog'); if (d) d.close(); return; }
            var copy = e.target.closest('[data-pems-copy]');
            if (copy) {
                var text = copy.getAttribute('data-pems-copy');
                if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text);
                return;
            }
            var dismiss = e.target.closest('[data-pems-dismiss]');
            if (dismiss) { var a = dismiss.closest('.pems-alert'); if (a) a.remove(); return; }
            document.querySelectorAll('details[data-pems-menu][open]').forEach(function (m) { if (!m.contains(e.target)) m.open = false; });
        });
    }

    // Bootstrap skin (the default): a Bootstrap modal has no native open hook, so the opener's row key is copied on Bootstrap's own event.
    document.addEventListener('show.bs.modal', function (e) {
        var opener = e.relatedTarget, key = opener ? opener.getAttribute('data-pems-row-key') : null;
        if (key === null) return;
        e.target.dataset.rowKey = key;
        e.target.querySelectorAll('[data-pems-row-key-target]').forEach(function (t) { t.value = key; });
    });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(); });
    else init();
    window.PemsOverlay = { init: init };
})();
