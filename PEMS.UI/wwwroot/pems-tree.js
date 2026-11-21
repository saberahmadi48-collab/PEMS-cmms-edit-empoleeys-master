// PEMS.UI tree (SCRUM-337 TreeView): <pems-tree> / <pems-tree-item>. Vanilla, offline, idempotent (dataset.pemsInit).
//   - the toggle button expands / collapses a branch; with data-pems-tree-remember the collapsed keys are kept in localStorage
//     (a branch that holds the current item is never collapsed on load)
//   - WAI-ARIA tree keyboard: one tab stop (roving tabindex); Up / Down move, Right opens or goes to the first child, Left closes
//     or goes to the parent, Home / End jump, Enter / Space follow the link (or toggle a branch without one)
(function () {
    'use strict';

    function read(key) { try { return JSON.parse(window.localStorage.getItem(key) || '[]'); } catch (e) { return []; } }
    function write(key, list) { try { window.localStorage.setItem(key, JSON.stringify(list)); } catch (e) { /* not remembered */ } }

    function group(li) { return li.querySelector(':scope > .pems-tree__group'); }
    function toggleOf(li) { return li.querySelector(':scope > .pems-tree__row > .pems-tree__toggle'); }
    function linkOf(li) { return li.querySelector(':scope > .pems-tree__row > .pems-tree__link'); }
    function isOpen(li) { return li.getAttribute('aria-expanded') === 'true'; }

    function setOpen(li, open) {
        var g = group(li), t = toggleOf(li);
        if (!g || !t) return;
        li.setAttribute('aria-expanded', open ? 'true' : 'false');
        t.setAttribute('aria-expanded', open ? 'true' : 'false');
        g.hidden = !open;
    }

    // items a keyboard user can reach: every item whose ancestors are all open
    function visibleItems(tree) {
        return Array.prototype.filter.call(tree.querySelectorAll('.pems-tree__item'), function (li) {
            for (var p = li.parentElement; p && p !== tree; p = p.parentElement) if (p.hidden) return false;
            return true;
        });
    }

    function focusItem(tree, li) {
        tree.querySelectorAll('.pems-tree__item[tabindex="0"]').forEach(function (x) { x.tabIndex = -1; });
        li.tabIndex = 0;
        li.focus();
    }

    function init(tree) {
        if (tree.dataset.pemsInit) return;
        tree.dataset.pemsInit = '1';
        var key = tree.getAttribute('data-pems-tree-remember');
        var collapsed = key ? read(key).map(String) : [];

        var items = tree.querySelectorAll('.pems-tree__item');
        items.forEach(function (li) {
            li.tabIndex = -1;
            var a = linkOf(li); if (a) a.tabIndex = -1;
            var t = toggleOf(li); if (t) t.tabIndex = -1;
            var k = li.getAttribute('data-key');
            if (k && collapsed.indexOf(k) >= 0 && !li.querySelector('[data-current]')) setOpen(li, false);
        });
        var start = tree.querySelector('.pems-tree__item[data-current]') || items[0];
        if (start) start.tabIndex = 0;

        function remember(li, open) {
            var k = li.getAttribute('data-key');
            if (!key || !k) return;
            collapsed = collapsed.filter(function (x) { return x !== k; });
            if (!open) collapsed.push(k);
            write(key, collapsed);
        }

        tree.addEventListener('click', function (e) {
            var t = e.target.closest('.pems-tree__toggle');
            if (!t || !tree.contains(t)) return;
            var li = t.closest('.pems-tree__item');
            var open = !isOpen(li);
            setOpen(li, open);
            remember(li, open);
        });

        tree.addEventListener('keydown', function (e) {
            var li = e.target.closest('.pems-tree__item');
            if (!li || e.target !== li) return;
            var list = visibleItems(tree), i = list.indexOf(li), branch = !!group(li);
            switch (e.key) {
                case 'ArrowDown': if (i < list.length - 1) focusItem(tree, list[i + 1]); break;
                case 'ArrowUp': if (i > 0) focusItem(tree, list[i - 1]); break;
                case 'Home': focusItem(tree, list[0]); break;
                case 'End': focusItem(tree, list[list.length - 1]); break;
                case 'ArrowRight':
                case 'ArrowLeft': {
                    // in a right-to-left page the arrows are mirrored, as the tree is
                    var rtl = getComputedStyle(tree).direction === 'rtl';
                    var forward = (e.key === 'ArrowRight') !== rtl;
                    if (forward) {
                        if (branch && !isOpen(li)) { setOpen(li, true); remember(li, true); }
                        else if (branch) focusItem(tree, group(li).querySelector('.pems-tree__item'));
                    } else {
                        if (branch && isOpen(li)) { setOpen(li, false); remember(li, false); }
                        else { var up = li.parentElement.closest('.pems-tree__item'); if (up) focusItem(tree, up); }
                    }
                    break;
                }
                case 'Enter':
                case ' ': {
                    var a = linkOf(li);
                    if (a) a.click();
                    else if (branch) { var open = !isOpen(li); setOpen(li, open); remember(li, open); }
                    break;
                }
                default: return;
            }
            e.preventDefault();
        });
    }

    function initAll(root) { (root || document).querySelectorAll('[data-pems-tree]').forEach(init); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { initAll(); });
    else initAll();
    window.PemsTree = { init: initAll };
})();
