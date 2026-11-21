// PEMS.UI searchable select (SCRUM-57). Loaded once by the layout, after the vendored Tom Select (lib/tom-select, Apache-2.0).
//
// Every single <select class="form-select"> with MIN_OPTIONS or more options becomes a searchable dropdown; the markup stays a
// plain <select>, so the form posts and validates as before and the page's own "change" handlers keep working.
//   data-pems-select           enhance whatever the size
//   data-pems-select="off"     never enhance (a page that drives the select itself)
//   data-pems-select-source="Vendor"   large list: options come from the server search (/SearchApi?type=Vendor), with the
//                              same permission rules; the selected option of the markup stays as the initial value
//   data-pems-select-url="/X/Lookup"   large list from a page endpoint (?q= → { items: [{ value, text, sub?, ... }] })
//   data-pems-select-create    a typed value is allowed (data-create-text: localised "use {0}")
// Texts are inputs: the layout puts the localised texts on <body data-pems-select-*>.
// Multi-selects keep their own initialiser (pems-multiselect.js).
(function () {
    'use strict';

    var MIN_OPTIONS = 8;
    var PAGE_SIZE = 20;
    var body = document.body;

    function text(name) { return (body && body.getAttribute('data-pems-select-' + name)) || ''; }

    function wanted(select) {
        if (select.tomselect || select.multiple || typeof window.TomSelect !== 'function') return false;
        var flag = select.getAttribute('data-pems-select');
        if (flag === 'off') return false;
        if (flag !== null || isRemote(select)) return true;
        return select.classList.contains('form-select') && select.options.length >= MIN_OPTIONS;
    }

    // inside a modal or a dropdown panel the list opens in place: Bootstrap's focus trap would take the focus back from a list
    // attached to <body>, and a click in it would count as "outside" and close the panel
    function inlineHost(select) { return select.closest('.modal, .dropdown-menu, [data-pems-select-inline]'); }

    function messageDiv(kind, msg) {
        var t = window.PemsTemplate;
        return t.fill(t.clone(kind === 'no-results' ? 'sel-no-results' : 'sel-loading'), { text: msg });
    }

    function isRemote(select) {
        return select.hasAttribute('data-pems-select-source') || select.hasAttribute('data-pems-select-url');
    }

    // data-pems-select-url: a page endpoint that answers ?q= with { items: [{ value, text, sub?, ...more }] }; the extra fields stay on
    // the option (tomselect.options[value]) for the page script, e.g. the description and unit of a part
    function urlOptions(select) {
        var url = select.getAttribute('data-pems-select-url');
        function pageUrl(q, page) { return url + (url.indexOf('?') < 0 ? '?' : '&') + 'q=' + encodeURIComponent(q) + (page > 1 ? '&page=' + page : ''); }
        return pagedOptions(1, pageUrl, function (data) { return (data.items || []); }, function (q) { return q.length >= 2; });
    }

    // SCRUM-513: results beyond the first page. Tom Select's virtual_scroll plugin asks for the next page when the list is scrolled to its end; the page
    // endpoint says hasMore (the SearchApi and a page endpoint that sends { items, hasMore } both do), so a list of 200,000 rows is read 20 at a time.
    function pagedOptions(startPage, pageUrl, toItems, shouldLoad) {
        return {
            plugins: ['virtual_scroll'],
            valueField: 'value',
            labelField: 'text',
            searchField: [],                 // the server ranks; Tom Select must not filter the page again
            preload: false,
            loadThrottle: 300,
            shouldLoad: shouldLoad,
            firstUrl: function (q) { return pageUrl(q, startPage); },
            load: function (q, callback) {
                var self = this;
                var current = self.getUrl(q);
                fetch(current, { headers: { 'Accept': 'application/json' } })
                    .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
                    .then(function (data) {
                        var page = Number(new URL(current, location.href).searchParams.get('page') || startPage);
                        if (data.hasMore) self.setNextUrl(q, pageUrl(q, page + 1));
                        callback(toItems(data));
                    })
                    .catch(function () { callback(); });
            }
        };
    }

    function remoteOptions(select) {
        if (select.hasAttribute('data-pems-select-url')) return urlOptions(select);
        var type = select.getAttribute('data-pems-select-source');
        var project = select.getAttribute('data-pems-select-project');
        function pageUrl(q, page) {
            var params = new URLSearchParams({ type: type, q: q, page: String(page), pageSize: String(PAGE_SIZE) });
            if (project) params.set('projectId', project);
            return '/SearchApi?' + params.toString();
        }
        return pagedOptions(1, pageUrl, function (data) {
            return (data.items || []).map(function (i) { return { value: String(i.id), text: i.label, sub: i.subLabel || '' }; });
        }, function (q) { return q.length >= 1; });
    }

    function init(select) {
        if (!wanted(select)) return;
        var remote = isRemote(select);
        var hasBlank = Array.prototype.some.call(select.options, function (o) { return o.value === ''; });
        var settings = {
            // "-- None --" / "All" stay real, selectable choices; a select without one never offers an empty choice
            // (Tom Select adds a blank <option> for its placeholder, which must not become selectable on a required field)
            allowEmptyOption: hasBlank,
            maxOptions: null,
            create: false,
            copyClassesToDropdown: false,
            dropdownParent: inlineHost(select) ? null : 'body',
            placeholder: select.getAttribute('data-placeholder') || text('placeholder'),
            searchField: ['text'],
            render: {
                no_results: function () { return messageDiv('no-results', text('no-results')); },
                loading: function () { return messageDiv('spinner-text', text('loading')); }
            },
            onInitialize: function () {
                // keep the accessible name of the original control on the typing box
                var label = select.id && document.querySelector('label[for="' + select.id + '"]');
                var name = select.getAttribute('aria-label') || (label && label.textContent.trim());
                if (name && this.control_input) this.control_input.setAttribute('aria-label', name);
            }
        };
        if (remote) {
            Object.assign(settings, remoteOptions(select));
            settings.render.option = function (item, escape) {
                return '<div>' + escape(item.text) + (item.sub ? '<small class="d-block text-muted">' + escape(item.sub) + '</small>' : '') + '</div>';
            };
        }
        // data-pems-select-create: a typed value is allowed (e.g. a part that is not catalogued yet); data-create-text is the
        // localised "use …" line with {0} for the typed text
        if (select.hasAttribute('data-pems-select-create')) {
            settings.create = true;
            settings.createOnBlur = true;
            settings.persist = false;
            var createText = select.getAttribute('data-create-text') || '{0}';
            settings.render.option_create = function (data, escape) {
                return '<div class="create">' + escape(createText).replace('{0}', '<strong>' + escape(data.input) + '</strong>') + '</div>';
            };
        }
        var ts = new window.TomSelect(select, settings);
        select.setAttribute('data-pems-select-ready', '');

        // a page that replaces the options (cascading dropdowns): re-read the <select>. Tom Select itself moves the selected <option>
        // node when a value is chosen, so only a real change of the option set (values and texts) triggers a re-read.
        function signature() {
            return Array.prototype.map.call(select.options, function (o) { return o.value + '\u0001' + o.text; }).sort().join('\u0002');
        }
        function refresh() {
            // clearOptions() makes Tom Select put a blank <option> into the select; keep a blank choice only if the page had one
            var pageHasBlank = Array.prototype.some.call(select.options, function (o) { return o.value === ''; });
            var pageValue = select.value;                 // what the page selected in its new options
            ts.clear(true);                               // clearOptions() keeps the selected item; drop it first (silently)
            ts.clearOptions();
            ts.sync();
            if (!pageHasBlank) ts.removeOption('');
            ts.setValue(pageValue, true);
            if (!pageHasBlank) {
                var blank = select.querySelector('option[value=""]');
                if (blank) blank.remove();
            }
            lastSignature = signature();
        }
        var lastSignature = signature();
        if (!remote) {
            new MutationObserver(function () { if (signature() !== lastSignature) refresh(); }).observe(select, { childList: true, subtree: true, characterData: true });
        }
        select.addEventListener('pems:select-refresh', refresh);
    }

    function scan(root) {
        if (root.matches && root.matches('select')) init(root);
        if (root.querySelectorAll) root.querySelectorAll('select').forEach(init);
    }

    function boot() {
        scan(document);
        // selects that arrive later: modals filled by fetch, partial reloads, rows added by a page script
        new MutationObserver(function (mutations) {
            mutations.forEach(function (m) {
                m.addedNodes.forEach(function (n) { if (n.nodeType === 1 && !n.closest('.ts-wrapper, .ts-dropdown')) scan(n); });
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    window.PemsSelect = { init: init, refresh: function (select) { select.dispatchEvent(new Event('pems:select-refresh')); } };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
