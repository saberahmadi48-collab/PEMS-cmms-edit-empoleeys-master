/* PEMS shared UI enhancer (global, tiny, no dependencies). One place that fixes cross-page defects instead of patching pages:
   1) Status badges: prepend an icon so a status is never colour-only. The icon comes from the Bootstrap semantic class
      (bg-* / text-bg-*) or from the PEMS status class (badge-status-*) - language independent. Numeric counters,
      tab counters, notification bubbles and sidebar badges are skipped.
   2) Icon-only controls (links/buttons with only an icon): give them an accessible name + tooltip from title/data-bs-title,
      or from the icon meaning (bilingual map).
   3) Form controls with no label/placeholder/title: derive an accessible name from the nearest visible label, else from the field name.
   4) Wide tables that are not already scrollable get a .table-responsive wrapper (no page-level horizontal scroll). */
(function () {
    'use strict';
    var isFa = (document.documentElement.lang || '').toLowerCase().indexOf('fa') === 0;

    var TONE_ICON = { success: 'bi-check-circle', danger: 'bi-x-circle', warning: 'bi-exclamation-triangle', info: 'bi-info-circle', primary: 'bi-arrow-repeat', neutral: 'bi-dash-circle' };

    function toneOf(el) {
        var m = /(?:^|\s)(?:text-)?bg-(success|danger|warning|info|primary|secondary|light|dark)(?:-subtle)?(?:\s|$)/.exec(el.className)
            || /(?:^|\s)text-bg-(success|danger|warning|info|primary|secondary|light|dark)(?:\s|$)/.exec(el.className);
        if (m) return (m[1] === 'secondary' || m[1] === 'light' || m[1] === 'dark') ? 'neutral' : m[1];
        var s = /badge-status-([A-Za-z]+)/.exec(el.className);
        if (s) {
            var n = s[1];
            if (/^(Approved|Issued|Released|ClientApproved|Completed|Active)$/.test(n)) return 'success';
            if (/^(Rejected|Overdue|Failed)$/.test(n)) return 'danger';
            if (/^(DueSoon|ReviseResubmit|OnHold)$/.test(n)) return 'warning';
            if (/^(Submitted|Checked|IDCReview|InProgress)$/.test(n)) return 'info';
            return 'neutral';
        }
        return null;
    }

    function enhanceBadges(root) {
        var els = (root || document).querySelectorAll('.pems-content .badge');
        for (var i = 0; i < els.length; i++) {
            var b = els[i];
            if (b.dataset.pemsIcon) continue;
            b.dataset.pemsIcon = '1';
            if (b.querySelector('i.bi, svg')) continue;
            if (b.closest('[role="tablist"], .nav-link, .pems-sidebar, .btn, .dropdown-item, .kpi-card')) continue;
            if (b.classList.contains('position-absolute')) continue;
            var text = (b.textContent || '').trim();
            if (!text || text.length > 28 || /^[\d.,%+\-/]+$/.test(text)) continue;
            var tone = toneOf(b);
            if (!tone) continue;
            var ic = window.PemsTemplate.clone('auto-icon');
            ic.classList.add(TONE_ICON[tone]);
            b.insertBefore(ic, b.firstChild);
        }
    }

    // icon meaning -> [English, Persian]
    var ICON_NAMES = {
        'pencil': ['Edit', 'ویرایش'], 'pencil-square': ['Edit', 'ویرایش'], 'trash': ['Delete', 'حذف'], 'trash3': ['Delete', 'حذف'], 'eye': ['View', 'مشاهده'],
        'x': ['Close', 'بستن'], 'x-lg': ['Close', 'بستن'], 'x-circle': ['Remove', 'حذف'], 'plus': ['Add', 'افزودن'], 'plus-lg': ['Add', 'افزودن'], 'plus-circle': ['Add', 'افزودن'],
        'download': ['Download', 'دانلود'], 'upload': ['Upload', 'آپلود'], 'arrow-repeat': ['Refresh', 'بازآوری'], 'arrow-clockwise': ['Refresh', 'بازآوری'],
        'search': ['Search', 'جستجو'], 'funnel': ['Filter', 'فیلتر'], 'three-dots': ['More actions', 'عملیات بیشتر'], 'three-dots-vertical': ['More actions', 'عملیات بیشتر'],
        'box-arrow-up-right': ['Open', 'باز کردن'], 'box-arrow-in-right': ['Open', 'باز کردن'], 'printer': ['Print', 'چاپ'], 'file-earmark-excel': ['Export to Excel', 'خروجی اکسل'],
        'check': ['Confirm', 'تأیید'], 'check-lg': ['Confirm', 'تأیید'], 'check-circle': ['Confirm', 'تأیید'], 'slash-circle': ['Disable', 'غیرفعال‌سازی'], 'ban': ['Disable', 'غیرفعال‌سازی'],
        'clock-history': ['History', 'تاریخچه'], 'link-45deg': ['Link', 'پیوند'], 'copy': ['Copy', 'کپی'], 'gear': ['Settings', 'تنظیمات'], 'arrow-left': ['Back', 'بازگشت'], 'arrow-right': ['Next', 'بعدی'],
        'chevron-down': ['Expand', 'باز کردن'], 'chevron-up': ['Collapse', 'بستن'], 'send': ['Send', 'ارسال'], 'paperclip': ['Attachment', 'پیوست'], 'question-circle': ['Help', 'راهنما'], 'info-circle': ['Details', 'جزئیات']
    };

    function iconName(e) {
        var i = e.querySelector('i.bi');
        if (!i) return null;
        var m = /(?:^|\s)bi-([a-z0-9-]+)/.exec(i.className);
        if (!m) return null;
        var pair = ICON_NAMES[m[1]];
        return pair ? pair[isFa ? 1 : 0] : null;
    }

    function nameIconOnlyControls(root) {
        var els = (root || document).querySelectorAll('a, button');
        for (var i = 0; i < els.length; i++) {
            var e = els[i];
            if (e.hasAttribute('aria-label') || e.hasAttribute('aria-labelledby')) continue;
            if ((e.textContent || '').trim()) continue;
            if (!e.querySelector('i.bi, svg')) continue;
            var t = e.getAttribute('title') || e.getAttribute('data-bs-title') || e.getAttribute('data-bs-original-title') || iconName(e);
            if (t) { e.setAttribute('aria-label', t); if (!e.getAttribute('title') && !e.getAttribute('data-bs-title')) e.setAttribute('title', t); }
        }
    }

    function humanize(s) {
        return (s || '').replace(/[\[\]._-]+/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/\s+/g, ' ').trim();
    }

    function nameFormControls(root) {
        var sel = '.pems-content input:not([type=hidden]):not([type=submit]):not([type=button]):not([type=checkbox]):not([type=radio]), .pems-content select, .pems-content textarea';
        var els = (root || document).querySelectorAll(sel);
        for (var i = 0; i < els.length; i++) {
            var e = els[i];
            if (e.getAttribute('aria-label') || e.getAttribute('aria-labelledby') || e.getAttribute('placeholder') || e.getAttribute('title')) continue;
            if (e.id && document.querySelector('label[for="' + e.id.replace(/"/g, '\\"') + '"]')) continue;
            if (e.closest('label')) continue;
            var name = null;
            var box = e.closest('.col, [class*="col-"], .mb-2, .mb-3, .form-group, .input-group') || e.parentElement;
            var lab = box && box.querySelector('label.form-label, label, .form-label');
            if (lab && (lab.textContent || '').trim()) name = lab.textContent.trim();
            else if (e.previousElementSibling && /^(LABEL|SPAN|DIV)$/.test(e.previousElementSibling.tagName) && (e.previousElementSibling.textContent || '').trim().length < 40) name = e.previousElementSibling.textContent.trim();
            else name = isFa ? 'فیلد' : humanize(e.getAttribute('name') || e.id);   // never leak an English field name into a Persian page
            if (name) e.setAttribute('aria-label', name);
        }
    }

    function wrapTables(root) {
        var tbs = (root || document).querySelectorAll('.pems-content table');
        for (var i = 0; i < tbs.length; i++) {
            var t = tbs[i];
            if (t.closest('.table-responsive, .pems-grid-scroll, .table-scroll-area, .overflow-auto, .overflow-x-auto, .modal, .offcanvas, .pems-scroll')) continue;
            if (t.dataset.pemsWrapped) continue;
            t.dataset.pemsWrapped = '1';
            var w = window.PemsTemplate.clone('auto-table-wrap');
            t.parentNode.insertBefore(w, t);
            w.appendChild(t);
        }
    }

    /* 5) Bootstrap dropdown menus: ONE shared positioning default. A menu is `position:absolute` by default, so inside .vs-panel
          (overflow:hidden) or a scroll wrapper (.pems-grid-scroll / .table-responsive: overflow:auto) it is clipped or covered by the
          next row / the pagination footer. Popper's `fixed` strategy places it in viewport coordinates, outside every overflow clip
          (reproduced + verified on Engineering Documents row menus). Never add per-page boundary/z-index workarounds. */
    function configureDropdowns() {
        var bs = window.bootstrap;
        if (!bs || !bs.Dropdown || !bs.Dropdown.Default || bs.Dropdown.Default.pemsFixed) return;
        var previous = bs.Dropdown.Default.popperConfig;
        bs.Dropdown.Default.popperConfig = function (defaults) {
            var base = typeof previous === 'function' ? previous(defaults) : (previous || defaults);
            return Object.assign({}, base, { strategy: 'fixed' });
        };
        bs.Dropdown.Default.pemsFixed = true;   // idempotent: a second initialisation must not wrap the function twice
    }

    function enhance(root) { enhanceBadges(root); nameIconOnlyControls(root); nameFormControls(root); wrapTables(root); }
    function run(root) { configureDropdowns(); enhance(root && root.nodeType === 1 ? root : undefined); }

    /* 6) Lifecycle: content inserted after load (fetch/AJAX partials, JS-built rows) gets the same enhancements. Debounced; only the
          parents of added nodes are rescanned; nodes created by this file (data-pems-auto) and transient Bootstrap UI are ignored,
          so the observer never reacts to its own changes. */
    var pending = [], timer = null;
    var SKIP = '.tooltip, .popover, .modal-backdrop, .offcanvas-backdrop, .toast, .dropdown-menu, .pc-lookup-list, script, style, link';
    function flush() {
        timer = null;
        var roots = pending; pending = [];
        for (var i = 0; i < roots.length; i++) { if (roots[i].isConnected) enhance(roots[i]); }
    }
    function observe() {
        if (!window.MutationObserver || !document.body) return;
        new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    var n = added[j];
                    if (n.nodeType !== 1 || (n.dataset && n.dataset.pemsAuto) || n.matches(SKIP)) continue;
                    var p = n.parentElement;
                    if (!p || (p.dataset && p.dataset.pemsAuto) || p.closest('.tooltip, .popover')) continue;
                    if (pending.indexOf(p) < 0) pending.push(p);
                }
            }
            if (pending.length && !timer) timer = setTimeout(flush, 120);
        }).observe(document.body, { childList: true, subtree: true });
    }

    /* 7) Tab state across POST -> redirect. Row actions post a form and the controller redirects to Index, which re-renders the page
          on its FIRST tab (reproduced on Maintenance Master Data: act on "Skills", land on "Groups"). When a POST form is submitted
          the active tab(s) are remembered and re-shown after the redirect - same path only, within 20 s, never over an explicit #hash.
          sessionStorage access is always guarded (private mode / blocked storage must not break the page). */
    var TAB_KEY = 'pems.tabs.v1';
    function tabTarget(btn) { return btn.getAttribute('data-bs-target') || btn.getAttribute('href'); }
    function rememberTabsOnSubmit() {
        document.addEventListener('submit', function (e) {
            var f = e.target;
            if (!f || !f.method || String(f.method).toLowerCase() !== 'post') return;
            var active = document.querySelectorAll('[data-bs-toggle="tab"].active, [data-bs-toggle="pill"].active');
            var targets = [];
            for (var i = 0; i < active.length; i++) { var t = tabTarget(active[i]); if (t && t.charAt(0) === '#') targets.push(t); }
            if (!targets.length) return;
            try { sessionStorage.setItem(TAB_KEY, JSON.stringify({ path: location.pathname, tabs: targets, at: Date.now() })); } catch (x) { /* storage unavailable */ }
        }, true);
    }
    function restoreTabs() {
        var raw = null;
        try { raw = sessionStorage.getItem(TAB_KEY); sessionStorage.removeItem(TAB_KEY); } catch (x) { return; }
        if (!raw || location.hash || !window.bootstrap || !window.bootstrap.Tab) return;
        var s; try { s = JSON.parse(raw); } catch (x) { return; }
        if (!s || s.path !== location.pathname || !s.tabs || Date.now() - s.at > 20000) return;
        for (var i = 0; i < s.tabs.length; i++) {
            var btn = document.querySelector('[data-bs-toggle="tab"][data-bs-target="' + s.tabs[i] + '"], [data-bs-toggle="tab"][href="' + s.tabs[i] + '"], [data-bs-toggle="pill"][data-bs-target="' + s.tabs[i] + '"], [data-bs-toggle="pill"][href="' + s.tabs[i] + '"]');
            if (btn && !btn.classList.contains('active')) window.bootstrap.Tab.getOrCreateInstance(btn).show();
        }
    }

    function init() { run(); observe(); rememberTabsOnSubmit(); restoreTabs(); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
    window.PemsUi = { refresh: run };
})();
