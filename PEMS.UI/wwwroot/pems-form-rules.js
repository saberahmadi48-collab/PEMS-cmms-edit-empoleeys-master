// PEMS.UI form engine in the browser (SCRUM-578, #578): <form data-pems-rules='{...}'> is written by the form component for a stored form definition. This file repeats, while the user types, the same
// conditions the server evaluates (visible, requiredWhen, readOnlyWhen; ADR-0015, pems-expr.js), so a field appears, becomes required or read only as soon as its condition changes. The server stays the
// authority: it evaluates the same conditions on the post, drops the values of hidden fields and refuses a missing required value, whatever this script did. Without the script the form still works.
(function () {
    'use strict';

    var ROOTS = ['values', 'user', 'request'];

    function number(text) {
        var s = String(text == null ? '' : text).trim();
        if (!s) return null;
        s = s.replace(/[۰-۹]/g, function (d) { return String(d.charCodeAt(0) - 0x06F0); }).replace(/[٠-٩]/g, function (d) { return String(d.charCodeAt(0) - 0x0660); }).replace(/٫/g, '.').replace(/,/g, '');
        var n = Number(s);
        return isNaN(n) ? null : n;
    }

    // the value of a field as the expression language sees it (the same typing as FormEngine.Typed on the server)
    function typed(form, field) {
        var els = form.elements[field.name];
        if (!els) return null;
        var el = els.length !== undefined && !els.tagName ? els : els;
        var list = el.length !== undefined && !el.tagName ? Array.prototype.slice.call(el) : [el];
        var first = list[0];
        if (field.kind === 'checkbox' || field.kind === 'switch') return list.some(function (e) { return e.checked; });
        if (field.kind === 'radio') { var on = list.filter(function (e) { return e.checked; })[0]; return on ? on.value : null; }
        var text = first.value;
        if (field.kind === 'number' || field.kind === 'money') return number(text);
        return text === '' || text == null ? null : text;
    }

    function compile(source) {
        if (!source) return null;
        try { return window.PemsExpr.compile(source, ROOTS); } catch (e) { return null; }
    }

    function init(form) {
        if (form.getAttribute('data-pems-rules-ready') !== null) return;
        var raw = form.getAttribute('data-pems-rules');
        if (!raw || !window.PemsExpr) return;
        var cfg;
        try { cfg = JSON.parse(raw); } catch (e) { return; }
        form.setAttribute('data-pems-rules-ready', '');
        var fields = (cfg.fields || []).map(function (f) {
            return { f: f, visible: compile(f.visible), required: compile(f.requiredWhen), readOnly: compile(f.readOnlyWhen) };
        });
        if (!fields.some(function (x) { return x.visible || x.required || x.readOnly; })) return;

        function holds(compiled, ctx) {
            if (!compiled) return false;
            try { return compiled.condition(ctx); } catch (e) { return false; }
        }
        function controls(name) {
            var found = form.elements[name];
            if (!found) return [];
            return found.length !== undefined && !found.tagName ? Array.prototype.slice.call(found) : [found];
        }

        function apply() {
            var values = {};
            fields.forEach(function (x) { values[x.f.name] = typed(form, x.f); });
            var ctx = { values: values, user: cfg.user || {}, request: cfg.request || {} };
            fields.forEach(function (x) {
                var visible = !x.visible || holds(x.visible, ctx);
                var required = !!x.f.required || holds(x.required, ctx);
                var readOnly = !!x.f.readOnly || holds(x.readOnly, ctx);
                var cell = form.querySelector('[data-field="' + x.f.name + '"]');
                if (cell) cell.hidden = !visible;
                controls(x.f.name).forEach(function (el) {
                    if (x.required || x.f.required) el.required = required && visible;
                    if (x.readOnly || x.f.readOnly) { if ('readOnly' in el && el.type !== 'checkbox' && el.type !== 'radio' && el.tagName !== 'SELECT') el.readOnly = readOnly; else el.disabled = readOnly; }
                });
            });
        }
        form.addEventListener('input', apply);
        form.addEventListener('change', apply);
        apply();
    }

    function scan(scope) { (scope || document).querySelectorAll('form[data-pems-rules]').forEach(init); }
    function boot() {
        scan(document);
        new MutationObserver(function (list) { list.forEach(function (m) { m.addedNodes.forEach(function (n) { if (n.nodeType === 1) { if (n.matches && n.matches('form[data-pems-rules]')) init(n); scan(n); } }); }); }).observe(document.body, { childList: true, subtree: true });
    }
    window.PemsFormRules = { init: init, scan: scan };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
