// PEMS declarative actions (SCRUM-347 / #333, ratchet counter on-handler): the page says WHAT happens in data attributes, this file does it.
// Replaces inline onclick / onchange / onsubmit handlers (no inline JS: prepares a strict Content-Security-Policy).
//   data-pems-click | data-pems-change | data-pems-submit = "<action>", with the parameters below.
//   print                          window.print()
//   back                           history.back()
//   prevent                        event.preventDefault()
//   submit-form                    submits the form of the element
//   call                           data-pems-fn="name" (dotted allowed), data-pems-arg="this|value|none", data-pems-args='["a","b"]' (appended)
//   toggle-hidden                  data-pems-target="#id", data-pems-hide-when="unchecked | value:X | value-not:X"
//   toggle-class                   data-pems-target="#id", data-pems-class="d-none"
//   check-all                      data-pems-target=".selector", data-pems-check="true | false | self"
(function () {
    'use strict';

    function lookup(name) {
        var parts = String(name || '').split('.'), owner = window, i;
        for (i = 0; i < parts.length - 1 && owner != null; i++) owner = owner[parts[i]];
        var fn = owner == null ? null : owner[parts[parts.length - 1]];
        return typeof fn === 'function' ? { fn: fn, owner: owner } : null;
    }

    var actions = {
        print: function () { window.print(); },
        back: function () { window.history.back(); },
        prevent: function (el, e) { e.preventDefault(); },
        'submit-form': function (el) { if (el.form) el.form.submit(); },
        call: function (el) {
            var target = lookup(el.getAttribute('data-pems-fn'));
            if (!target) return;
            var arg = el.getAttribute('data-pems-arg') || 'none', args = [];
            if (arg === 'this') args.push(el); else if (arg === 'value') args.push(el.value);
            var extra = el.getAttribute('data-pems-args');
            if (extra) { try { args = args.concat(JSON.parse(extra)); } catch (x) { /* a malformed args list calls with the rest */ } }
            target.fn.apply(target.owner, args);
        },
        'toggle-hidden': function (el) {
            var t = document.querySelector(el.getAttribute('data-pems-target') || ''), rule = el.getAttribute('data-pems-hide-when') || '';
            if (!t) return;
            var hide;
            if (rule === 'unchecked') hide = !el.checked;
            else if (rule.indexOf('value:') === 0) hide = el.value === rule.slice(6);
            else if (rule.indexOf('value-not:') === 0) hide = el.value !== rule.slice(10);
            else return;
            t.hidden = hide;
        },
        'toggle-class': function (el) {
            var t = document.querySelector(el.getAttribute('data-pems-target') || ''), c = el.getAttribute('data-pems-class');
            if (t && c) t.classList.toggle(c);
        },
        'check-all': function (el) {
            var v = el.getAttribute('data-pems-check'), on = v === 'self' ? el.checked : v === 'true';
            document.querySelectorAll(el.getAttribute('data-pems-target') || '').forEach(function (c) { c.checked = on; });
        }
    };

    ['click', 'change', 'submit'].forEach(function (type) {
        document.addEventListener(type, function (e) {
            var el = e.target && e.target.closest ? e.target.closest('[data-pems-' + type + ']') : null;
            if (!el) return;
            var run = actions[el.getAttribute('data-pems-' + type)];
            if (run) run(el, e);
        });
    });
})();
