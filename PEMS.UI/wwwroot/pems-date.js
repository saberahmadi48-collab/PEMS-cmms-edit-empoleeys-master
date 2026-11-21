// PEMS.UI Jalali date field (SCRUM-225, #206). Starts the vendored picker (lib/jalalidatepicker, MIT) on the inputs the pems-date tag helper rendered for a Jalali page:
//   <input data-jdp data-jdp-target-value-input="#x-iso" data-jdp-target-value-type="gregorian" value="1403/12/30"> + <input type="hidden" id="x-iso" name="due" value="2025-03-20" data-pems-date-iso>
// The visible text is Jalali, the posted value is the Gregorian ISO date in the hidden input. Two ways write it: the picker (when a day is chosen) and this script (when a date is typed:
// Persian, Arabic or Latin digits, separators / - . or space; an impossible date such as 1404/12/30 marks the field invalid and leaves the hidden value empty).
// Conversion uses the browser's own Persian calendar (Intl, ICU), checked against .NET PersianCalendar for the years 1300 to 1500 (PEMS.Tests JalaliDateTests); no calendar code of our own.
// Safe fallback: if the library did not load or fails to start, every such pair becomes the native date input again (same name, ISO value), so a date can always be entered.
// Texts are inputs: <body data-pems-date-invalid="..."> (the layout). Vanilla, offline, no inline handlers.
// SCRUM-543 date and time: <input data-jdp data-pems-date-time data-pems-date-target="#x-iso" value="1403/12/30 14:30"> + hidden "2025-03-20T14:30". This script writes
// the hidden value (the picker does not get a target for these), the picker shows its time panel only for them (the date-only fields get data-jdp-only-date at start),
// and the fallback is the native datetime-local input. PemsDate.value(el) / PemsDate.setValue(el, iso) read and write the ISO value of any pems-date field for other scripts.
(function () {
    'use strict';

    var fmt = null;
    try { fmt = new Intl.DateTimeFormat('en-u-ca-persian-nu-latn', { year: 'numeric', month: 'numeric', day: 'numeric', timeZone: 'UTC' }); } catch (e) { fmt = null; }

    function parts(ms) {
        var o = {};
        fmt.formatToParts(new Date(ms)).forEach(function (p) { if (p.type !== 'literal') o[p.type] = parseInt(p.value, 10); });
        return o;
    }

    function doy(m, d) { return (m <= 6 ? (m - 1) * 31 : 186 + (m - 7) * 30) + d; }

    function pad(n, w) { var s = String(n); while (s.length < w) s = '0' + s; return s; }

    // Jalali y/m/d -> ISO yyyy-MM-dd, or '' when the date does not exist in the Persian calendar (round trip check)
    function jalaliToIso(y, m, d) {
        if (!fmt || m < 1 || m > 12 || d < 1 || d > 31 || y < 1 || y > 3000) return '';
        var ms = Date.UTC(y + 621, 2, 21) + (doy(m, d) - 1) * 86400000;
        for (var i = 0; i < 6; i++) {
            var p = parts(ms);
            if (p.year === y && p.month === m && p.day === d) {
                var g = new Date(ms);
                return pad(g.getUTCFullYear(), 4) + '-' + pad(g.getUTCMonth() + 1, 2) + '-' + pad(g.getUTCDate(), 2);
            }
            ms += (p.year < y ? 1 : p.year > y ? -1 : doy(m, d) - doy(p.month, p.day)) * 86400000;
        }
        return '';
    }

    function latinDigits(text) {
        return String(text || '').replace(/[۰-۹]/g, function (c) { return String(c.charCodeAt(0) - 0x06F0); })
            .replace(/[٠-٩]/g, function (c) { return String(c.charCodeAt(0) - 0x0660); }).replace(/[‎‏]/g, '').trim();
    }

    // typed text -> [y, m, d] or null; digits of any script, separators / - . and space
    function parseTyped(text) {
        var m = /^(\d{4})[\/\-. ](\d{1,2})[\/\-. ](\d{1,2})$/.exec(latinDigits(text));
        return m ? [parseInt(m[1], 10), parseInt(m[2], 10), parseInt(m[3], 10)] : null;
    }

    // SCRUM-543: typed date and time -> [y, m, d, hh, mm] or null; the time may be left out (00:00) and may have seconds (ignored)
    function parseTypedDateTime(text) {
        var m = /^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})(?:[ T]+(\d{1,2}):(\d{2})(?::\d{2})?)?$/.exec(latinDigits(text));
        if (!m) return null;
        var hh = m[4] ? parseInt(m[4], 10) : 0, mm = m[5] ? parseInt(m[5], 10) : 0;
        if (hh > 23 || mm > 59) return null;
        return [parseInt(m[1], 10), parseInt(m[2], 10), parseInt(m[3], 10), hh, mm];
    }

    // ISO yyyy-MM-dd (optionally THH:mm) -> Jalali text yyyy/MM/dd (HH:mm), or '' when the text is not a date
    function isoToJalaliText(iso) {
        var m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/.exec(String(iso || ''));
        if (!fmt || !m) return '';
        var p = parts(Date.UTC(parseInt(m[1], 10), parseInt(m[2], 10) - 1, parseInt(m[3], 10)));
        return pad(p.year, 4) + '/' + pad(p.month, 2) + '/' + pad(p.day, 2) + (m[4] ? ' ' + m[4] + ':' + m[5] : '');
    }

    function isTime(visible) { return visible.hasAttribute('data-pems-date-time'); }

    function pairs() {
        return Array.prototype.slice.call(document.querySelectorAll('input[data-jdp][data-jdp-target-value-input], input[data-jdp][data-pems-date-target]'));
    }

    function hiddenOf(visible) {
        var sel = visible.getAttribute('data-jdp-target-value-input') || visible.getAttribute('data-pems-date-target');
        try { return sel ? document.querySelector(sel) : null; } catch (e) { return null; }
    }

    function message() {
        return (document.body && document.body.getAttribute('data-pems-date-invalid')) || 'This date does not exist in the Jalali calendar. Use year/month/day, for example 1403/12/29.';
    }

    // typed text -> hidden ISO value; returns false when the text is not a valid date
    function sync(visible) {
        var hidden = hiddenOf(visible);
        if (!hidden) return true;
        var text = visible.value;
        if (!String(text).trim()) { hidden.value = ''; visible.setCustomValidity(''); visible.classList.remove('is-invalid'); return true; }
        if (isTime(visible)) return syncDateTime(visible, hidden, text);
        var t = parseTyped(text);
        var iso = t ? jalaliToIso(t[0], t[1], t[2]) : '';
        if (!iso) { hidden.value = ''; visible.setCustomValidity(message()); visible.classList.add('is-invalid'); return false; }
        var min = visible.getAttribute('data-pems-date-min'), max = visible.getAttribute('data-pems-date-max');
        if ((min && iso < min) || (max && iso > max)) { hidden.value = ''; visible.setCustomValidity(message()); visible.classList.add('is-invalid'); return false; }
        hidden.value = iso;
        visible.value = pad(t[0], 4) + '/' + pad(t[1], 2) + '/' + pad(t[2], 2);
        visible.setCustomValidity('');
        visible.classList.remove('is-invalid');
        return true;
    }

    // SCRUM-543: date and time -> hidden yyyy-MM-ddTHH:mm, visible normalised to yyyy/MM/dd HH:mm
    function syncDateTime(visible, hidden, text) {
        var t = parseTypedDateTime(text);
        var day = t ? jalaliToIso(t[0], t[1], t[2]) : '';
        if (!day) { hidden.value = ''; visible.setCustomValidity(message()); visible.classList.add('is-invalid'); return false; }
        hidden.value = day + 'T' + pad(t[3], 2) + ':' + pad(t[4], 2);
        visible.value = pad(t[0], 4) + '/' + pad(t[1], 2) + '/' + pad(t[2], 2) + ' ' + pad(t[3], 2) + ':' + pad(t[4], 2);
        visible.setCustomValidity('');
        visible.classList.remove('is-invalid');
        return true;
    }

    // the ISO value of a pems-date field (the hidden input of a Jalali pair, else the input itself)
    function value(el) {
        if (!el) return '';
        var hidden = el.hasAttribute('data-jdp') ? hiddenOf(el) : null;
        if (hidden) { sync(el); return hidden.value; }
        return el.value;
    }

    // set a pems-date field from an ISO date or date-time
    function setValue(el, iso) {
        if (!el) return;
        var hidden = el.hasAttribute('data-jdp') ? hiddenOf(el) : null;
        if (!hidden) { el.value = iso || ''; return; }
        hidden.value = iso || '';
        el.value = iso ? isoToJalaliText(iso) : '';
        el.setCustomValidity('');
        el.classList.remove('is-invalid');
    }

    function fallback() {
        pairs().forEach(function (visible) {
            var hidden = hiddenOf(visible);
            if (!hidden) return;
            var iso = hidden.value;
            visible.removeAttribute('data-jdp');
            visible.removeAttribute('data-jdp-target-value-input');
            visible.removeAttribute('data-jdp-target-value-type');
            visible.removeAttribute('data-pems-date-target');
            visible.removeAttribute('inputmode');
            visible.removeAttribute('placeholder');
            visible.removeAttribute('dir');
            visible.setAttribute('type', isTime(visible) ? 'datetime-local' : 'date');
            visible.setAttribute('name', hidden.getAttribute('name') || '');
            visible.value = iso;
            ['min', 'max'].forEach(function (k) {
                var v = visible.getAttribute('data-pems-date-' + k);
                if (v) visible.setAttribute(k, v);
                visible.removeAttribute('data-pems-date-' + k);
                visible.removeAttribute('data-jdp-' + k + '-date');
            });
            hidden.parentNode.removeChild(hidden);
        });
    }

    function wireTyping() {
        document.addEventListener('change', function (e) { if (e.target && e.target.matches && e.target.matches('input[data-jdp]')) sync(e.target); }, true);
        document.addEventListener('jdp:change', function (e) { if (e.target && e.target.matches && e.target.matches('input[data-jdp]')) sync(e.target); }, true);
        document.addEventListener('blur', function (e) { if (e.target && e.target.matches && e.target.matches('input[data-jdp]')) sync(e.target); }, true);
        // Enter in the field submits before a blur: sync every field of the form first and stop the submit when one is invalid
        document.addEventListener('submit', function (e) {
            var bad = null;
            pairs().forEach(function (v) { if (v.form === e.target && !sync(v) && !bad) bad = v; });
            if (bad) { e.preventDefault(); bad.reportValidity(); }
        }, true);
    }

    function start() {
        if (!pairs().length) return;
        if (!window.jalaliDatepicker || typeof window.jalaliDatepicker.startWatch !== 'function' || !fmt) { fallback(); return; }
        // SCRUM-543: the time panel is on only when the page has a date-and-time field; the date-only fields keep their date-only picker
        var anyTime = pairs().some(isTime);
        if (anyTime) pairs().forEach(function (v) { if (!isTime(v)) v.setAttribute('data-jdp-only-date', ''); });
        try {
            window.jalaliDatepicker.startWatch({
                selector: 'input[data-jdp]',
                minDate: 'attr',
                maxDate: 'attr',
                targetValueInput: 'attr',
                targetValueType: 'attr',
                persianDigits: false,
                showTodayBtn: true,
                showEmptyBtn: true,
                time: anyTime,
                zIndex: 2100
            });
            wireTyping();
        } catch (e) {
            if (window.console) console.error(e);
            fallback();
        }
    }

    window.PemsDate = { jalaliToIso: jalaliToIso, parseTyped: parseTyped, parseTypedDateTime: parseTypedDateTime, isoToJalaliText: isoToJalaliText, value: value, setValue: setValue };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
