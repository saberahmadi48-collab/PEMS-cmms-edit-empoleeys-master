// PEMS.UI skeleton (SCRUM-337): the grey placeholder a script-filled panel shows while its data is on the way.
// The same markup as the <pems-skeleton> tag helper (PemsSkeletonMarkup). Vanilla, offline, no inline handlers.
//   PemsSkeleton.html(kind, opts)        -> markup string; kind: text | title | card | kpi | table | form
//                                           opts: { lines: 3, rows: 5, cols: 4, label: "Loading..." }
//   PemsSkeleton.show(element, kind, opts) puts it into element (replaces its content)
// The screen-reader label comes from opts.label or <body data-pems-skeleton-label="...">, set by the layout in the
// current language.
(function () {
    'use strict';

    var KINDS = ['text', 'title', 'card', 'kpi', 'table', 'form'];

    function clamp(n, lo, hi, d) { n = parseInt(n, 10); if (isNaN(n)) n = d; return Math.max(lo, Math.min(hi, n)); }
    // the pieces are the page's registered templates (script-templates, set shell); no markup string lives here
    function tpl(name) { return window.PemsTemplate.clone(name); }
    function bar(extra) { var b = tpl('skel-bar'); if (extra) extra.trim().split(' ').forEach(function (c) { b.classList.add(c); }); return b; }

    function defaultLabel() {
        var b = document.body;
        return b ? (b.getAttribute('data-pems-skeleton-label') || '') : '';
    }

    function build(kind, opts) {
        opts = opts || {};
        var k = KINDS.indexOf(kind) >= 0 ? kind : 'text';
        var lines = clamp(opts.lines, 1, 20, 3), rows = clamp(opts.rows, 1, 30, 5), cols = clamp(opts.cols, 1, 12, 4);
        var label = opts.label != null ? opts.label : defaultLabel();
        var out = tpl('skel-root');
        out.classList.add('pems-skeleton--' + k);
        if (label) out.appendChild(window.PemsTemplate.fill(tpl('skel-label'), { text: label }));
        var i, r, c, stack, row;
        switch (k) {
            case 'title':
                out.appendChild(bar(' pems-skeleton__line--title'));
                break;
            case 'card':
                out.appendChild(bar(' pems-skeleton__line--title'));
                for (i = 0; i < 3; i++) out.appendChild(bar(i === 2 ? ' pems-skeleton__line--short' : ''));
                break;
            case 'kpi':
                out.appendChild(bar(' pems-skeleton__line--square'));
                stack = tpl('skel-stack');
                stack.appendChild(bar(' pems-skeleton__line--value'));
                stack.appendChild(bar(' pems-skeleton__line--short'));
                out.appendChild(stack);
                break;
            case 'table':
                for (r = 0; r < rows; r++) {
                    row = tpl('skel-row');
                    for (c = 0; c < cols; c++) row.appendChild(bar());
                    out.appendChild(row);
                }
                break;
            case 'form':
                for (r = 0; r < rows; r++) { out.appendChild(bar(' pems-skeleton__line--label')); out.appendChild(bar(' pems-skeleton__line--field')); }
                break;
            default:
                for (i = 0; i < lines; i++) out.appendChild(bar(i === lines - 1 && lines > 1 ? ' pems-skeleton__line--short' : ''));
        }
        return out;
    }

    function html(kind, opts) { return build(kind, opts).outerHTML; }

    function show(element, kind, opts) {
        if (!element) return;
        element.replaceChildren(build(kind, opts));
    }

    window.PemsSkeleton = { html: html, show: show, kinds: KINDS.slice() };
})();
