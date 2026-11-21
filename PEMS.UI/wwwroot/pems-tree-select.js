// PEMS.UI tree select (SCRUM-513). Loaded once by the layout. A field that picks one node of a large tree (functional location, organization unit, PBS, equipment)
// without loading the tree: <div data-pems-tree-select data-url="/FunctionalLocation/TreeNodes"> written by <pems-tree-select>.
//   GET url?parentId=&q=&page=  ->  { items: [{ value, text, sub?, hasChildren }], hasMore }      (the server applies the user's permissions)
//   No parentId: the roots. A level is read 20 nodes at a time ("more" asks the next page). A search of two characters or more is a flat list over the whole tree.
// Keyboard (WAI-ARIA tree): Down/Up move, Right opens or enters a node, Left closes or goes to the parent, Home/End, Enter or Space picks, Escape closes.
// The picked id goes to the hidden input (a "change" event bubbles) and its text to the button. Texts are the page's own (data-loading, data-no-results, data-more, data-clear).
(function () {
    'use strict';

    var SEARCH_MIN = 2;
    var DELAY = 300;

    // the markup of a node and of the "more" row is the registered script template (ts-item, ts-more); this file only fills it
    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }

    function init(root) {
        if (root.getAttribute('data-pems-tree-select-ready') !== null) return;
        root.setAttribute('data-pems-tree-select-ready', '');
        var url = root.getAttribute('data-url');
        var hidden = root.querySelector('[data-pems-tree-select-value]');
        var button = root.querySelector('.pems-tree-select__button');
        var panel = root.querySelector('.pems-tree-select__panel');
        var search = root.querySelector('.pems-tree-select__search');
        var tree = root.querySelector('.pems-tree-select__tree');
        var status = root.querySelector('.pems-tree-select__status');
        var texts = {
            loading: root.getAttribute('data-loading') || '',
            none: root.getAttribute('data-no-results') || '',
            more: root.getAttribute('data-more') || '',
            clear: root.getAttribute('data-clear') || '',
            placeholder: root.getAttribute('data-placeholder') || ''
        };
        var loaded = false;
        var timer = null;
        var token = 0;   // a late answer of an older search must not overwrite the newer list

        function say(text) { status.textContent = text || ''; }

        function request(parentId, q, page) {
            var params = new URLSearchParams();
            if (parentId) params.set('parentId', parentId);
            if (q) params.set('q', q);
            if (page > 1) params.set('page', String(page));
            return fetch(url + (url.indexOf('?') < 0 ? '?' : '&') + params.toString(), { headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); });
        }

        function items() { return Array.prototype.slice.call(tree.querySelectorAll('[role="treeitem"]')).filter(visible); }
        function visible(li) {
            for (var p = li.parentElement; p && p !== tree; p = p.parentElement) if (p.hidden) return false;
            return true;
        }
        function focusItem(li) {
            items().forEach(function (x) { x.setAttribute('tabindex', '-1'); });
            if (!li) return;
            li.setAttribute('tabindex', '0');
            li.focus();
        }

        function nodeItem(data, level) {
            var li = tpl('ts-item', {
                label: data.text,
                sub: data.sub ? data.sub : null,
                toggle: data.hasChildren ? '' : null,
                spacer: data.hasChildren ? null : '',
                group: data.hasChildren ? '' : null
            });
            li.setAttribute('aria-level', String(level));
            li.setAttribute('aria-selected', String(hidden.value === data.value));
            li.setAttribute('data-value', data.value);
            li.setAttribute('data-text', data.text);
            if (data.hasChildren) li.setAttribute('aria-expanded', 'false');
            return li;
        }

        // one page of children (or of a search) into a list; "more" asks the next page of the same list
        function fill(list, parentId, q, page, level, flat, mine) {
            say(texts.loading);
            return request(parentId, q, page).then(function (data) {
                if (mine !== token) return;
                var more = list.querySelector(':scope > .pems-tree-select__more-item');
                if (more) more.remove();
                (data.items || []).forEach(function (d) { list.appendChild(nodeItem(d, level)); });
                if (data.hasMore) {
                    var row = tpl('ts-more', { button: texts.more });
                    row.querySelector('button').addEventListener('click', function () { token++; fill(list, parentId, q, page + 1, level, flat, token); });
                    list.appendChild(row);
                }
                say(list.querySelector('[role="treeitem"]') ? '' : texts.none);
                if (page === 1 && !tree.querySelector('[tabindex="0"]')) { var first = items()[0]; if (first) first.setAttribute('tabindex', '0'); }
            }).catch(function () { if (mine === token) say(texts.none); });
        }

        function loadRoots() {
            token++;
            tree.textContent = '';
            return fill(tree, '', '', 1, 1, false, token);
        }

        function runSearch() {
            var q = search.value.trim();
            if (q.length < SEARCH_MIN) return loadRoots();
            token++;
            tree.textContent = '';
            return fill(tree, '', q, 1, 1, true, token);
        }

        function expand(li) {
            if (li.getAttribute('aria-expanded') !== 'false') return;
            var group = li.querySelector(':scope > .pems-tree-select__group');
            li.setAttribute('aria-expanded', 'true');
            group.hidden = false;
            if (!group.getAttribute('data-loaded')) {
                group.setAttribute('data-loaded', '');
                token++;
                fill(group, li.getAttribute('data-value'), '', 1, Number(li.getAttribute('aria-level')) + 1, false, token);
            }
        }
        function collapse(li) {
            if (li.getAttribute('aria-expanded') !== 'true') return;
            li.setAttribute('aria-expanded', 'false');
            li.querySelector(':scope > .pems-tree-select__group').hidden = true;
        }

        function pick(li) {
            hidden.value = li ? li.getAttribute('data-value') : '';
            button.textContent = li ? li.getAttribute('data-text') : texts.placeholder;
            hidden.dispatchEvent(new Event('change', { bubbles: true }));
            close(true);
        }

        function open() {
            if (button.disabled) return;
            panel.hidden = false;
            button.setAttribute('aria-expanded', 'true');
            if (!loaded) { loaded = true; loadRoots(); }
            search.focus();
        }
        function close(focusButton) {
            panel.hidden = true;
            button.setAttribute('aria-expanded', 'false');
            if (focusButton) button.focus();
        }

        button.addEventListener('click', function () { if (panel.hidden) open(); else close(true); });
        button.addEventListener('keydown', function (e) { if (e.key === 'ArrowDown' && panel.hidden) { e.preventDefault(); open(); } });
        search.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(runSearch, DELAY); });

        tree.addEventListener('click', function (e) {
            var li = e.target.closest('[role="treeitem"]');
            if (!li || !tree.contains(li)) return;
            if (e.target.closest('.pems-tree-select__toggle')) { if (li.getAttribute('aria-expanded') === 'true') collapse(li); else expand(li); return; }
            pick(li);
        });

        panel.addEventListener('keydown', function (e) {
            var current = e.target.closest && e.target.closest('[role="treeitem"]');
            var list = items();
            var at = current ? list.indexOf(current) : -1;
            if (e.key === 'Escape') { e.preventDefault(); close(true); return; }
            if (e.key === 'ArrowDown') { e.preventDefault(); focusItem(list[Math.min(at + 1, list.length - 1)]); return; }
            if (e.key === 'ArrowUp') { e.preventDefault(); if (at <= 0) search.focus(); else focusItem(list[at - 1]); return; }
            if (!current) return;
            if (e.key === 'Home') { e.preventDefault(); focusItem(list[0]); }
            else if (e.key === 'End') { e.preventDefault(); focusItem(list[list.length - 1]); }
            else if (e.key === 'ArrowRight') {
                e.preventDefault();
                if (current.getAttribute('aria-expanded') === 'false') expand(current);
                else if (current.getAttribute('aria-expanded') === 'true') { var child = current.querySelector('[role="treeitem"]'); if (child) focusItem(child); }
            } else if (e.key === 'ArrowLeft') {
                e.preventDefault();
                if (current.getAttribute('aria-expanded') === 'true') collapse(current);
                else { var parent = current.parentElement && current.parentElement.closest('[role="treeitem"]'); if (parent) focusItem(parent); }
            } else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(current); }
        });

        document.addEventListener('click', function (e) { if (!panel.hidden && !root.contains(e.target)) close(false); });
    }

    function scan(scope) {
        (scope || document).querySelectorAll('[data-pems-tree-select]').forEach(init);
    }

    function boot() {
        scan(document);
        new MutationObserver(function (mutations) {
            mutations.forEach(function (m) { m.addedNodes.forEach(function (n) { if (n.nodeType === 1) { if (n.matches && n.matches('[data-pems-tree-select]')) init(n); scan(n); } }); });
        }).observe(document.body, { childList: true, subtree: true });
    }

    window.PemsTreeSelect = { init: init, scan: scan };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
