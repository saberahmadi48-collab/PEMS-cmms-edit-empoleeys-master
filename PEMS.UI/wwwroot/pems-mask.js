// PEMS.UI input masks (SCRUM-386): <input data-pems-mask="9999-9999"> shows the value grouped as the user types. 9 = a digit, A = a letter, * = a letter or digit, anything else is a separator.
// Persian and Arabic digits become Latin. The server cleans the posted value with the same rule (FieldMask.Canonical), so without this script the form still works. No markup is written here.
(function () {
    'use strict';

    function latin(s) { return String(s).replace(/[\u06F0-\u06F9]/g, function (d) { return String(d.charCodeAt(0) - 0x06F0); }).replace(/[\u0660-\u0669]/g, function (d) { return String(d.charCodeAt(0) - 0x0660); }); }
    function fits(c, slot) { return slot === '9' ? /[0-9]/.test(c) : slot === 'A' ? /\p{L}/u.test(c) : /[\p{L}0-9]/u.test(c); }
    function format(value, mask) {
        var raw = latin(value).replace(/[^\p{L}0-9]/gu, ''), out = '', i = 0;
        for (var k = 0; k < mask.length; k++) {
            if (i >= raw.length) break;
            var m = mask.charAt(k);
            if (m === '9' || m === 'A' || m === '*') {
                while (i < raw.length && !fits(raw.charAt(i), m)) i++;
                if (i >= raw.length) break;
                out += raw.charAt(i++);
            } else out += m;
        }
        return out.replace(/[^\p{L}0-9]+$/u, '');
    }
    function attach(el) {
        if (el.dataset.pemsMaskInit) return;
        el.dataset.pemsMaskInit = '1';
        var mask = el.getAttribute('data-pems-mask');
        if (!mask) return;
        if (!el.getAttribute('maxlength')) el.setAttribute('maxlength', String(mask.length));
        el.value = format(el.value, mask);
        el.addEventListener('input', function () {
            var atEnd = el.selectionStart === el.value.length;
            el.value = format(el.value, mask);
            if (atEnd) el.setSelectionRange(el.value.length, el.value.length);
        });
    }
    function scan(scope) { (scope || document).querySelectorAll('[data-pems-mask]').forEach(attach); }
    window.PemsMask = { format: format, scan: scan };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { scan(); }); else scan();
})();
