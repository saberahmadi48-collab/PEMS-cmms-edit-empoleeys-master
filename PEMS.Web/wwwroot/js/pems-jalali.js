// Jalali (Persian) calendar helpers shared by pages that take dates from users. The server always stores/receives Gregorian ISO (yyyy-MM-dd);
// these functions only convert for display and for the date picker. Algorithm: Borkowski's integer conversion (valid for 1 Farvardin 1 .. 3000).
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsJalali = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var G_D_M = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];

    function div(a, b) { return Math.trunc(a / b); }

    function toJalali(gy, gm, gd) {
        var gy2 = gm > 2 ? gy + 1 : gy;
        var days = 355666 + 365 * gy + div(gy2 + 3, 4) - div(gy2 + 99, 100) + div(gy2 + 399, 400) + gd + G_D_M[gm - 1];
        var jy = -1595 + 33 * div(days, 12053);
        days %= 12053;
        jy += 4 * div(days, 1461);
        days %= 1461;
        if (days > 365) { jy += div(days - 1, 365); days = (days - 1) % 365; }
        var jm = days < 186 ? 1 + div(days, 31) : 7 + div(days - 186, 30);
        var jd = 1 + (days < 186 ? days % 31 : (days - 186) % 30);
        return { y: jy, m: jm, d: jd };
    }

    function toGregorian(jy, jm, jd) {
        jy += 1595;
        var days = -355668 + 365 * jy + div(jy, 33) * 8 + div((jy % 33) + 3, 4) + jd + (jm < 7 ? (jm - 1) * 31 : (jm - 7) * 30 + 186);
        var gy = 400 * div(days, 146097);
        days %= 146097;
        if (days > 36524) {
            days--;
            gy += 100 * div(days, 36524);
            days %= 36524;
            if (days >= 365) days++;
        }
        gy += 4 * div(days, 1461);
        days %= 1461;
        if (days > 365) { gy += div(days - 1, 365); days = (days - 1) % 365; }
        var gd = days + 1;
        var leap = (gy % 4 === 0 && gy % 100 !== 0) || gy % 400 === 0;
        var sal = [0, 31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        var gm = 0;
        while (gm < 13 && gd > sal[gm]) { gd -= sal[gm]; gm++; }
        return { y: gy, m: gm, d: gd };
    }

    function isLeap(jy) {
        // Esfand has 30 days exactly when 30 Esfand round-trips to the same Jalali date
        var g = toGregorian(jy, 12, 30);
        var back = toJalali(g.y, g.m, g.d);
        return back.y === jy && back.m === 12 && back.d === 30;
    }

    function daysInMonth(jy, jm) {
        if (jm <= 6) return 31;
        if (jm <= 11) return 30;
        return isLeap(jy) ? 30 : 29;
    }

    function pad(n, w) { var s = String(n); while (s.length < w) s = "0" + s; return s; }

    function formatIso(g) { return pad(g.y, 4) + "-" + pad(g.m, 2) + "-" + pad(g.d, 2); }
    function formatJalali(j) { return pad(j.y, 4) + "/" + pad(j.m, 2) + "/" + pad(j.d, 2); }

    // Persian/Arabic-Indic digits -> ASCII
    function normalizeDigits(text) {
        return String(text || "").replace(/[۰-۹]/g, function (c) { return String(c.charCodeAt(0) - 0x06F0); })
            .replace(/[٠-٩]/g, function (c) { return String(c.charCodeAt(0) - 0x0660); });
    }

    // "1405/02/05" (or "-" separated, any digits) -> {y,m,d} or null when it is not a real Jalali date
    function parseJalali(text) {
        var parts = normalizeDigits(text).trim().split(/[\/\-.]/);
        if (parts.length !== 3) return null;
        var y = parseInt(parts[0], 10), m = parseInt(parts[1], 10), d = parseInt(parts[2], 10);
        if (!(y >= 1 && y <= 3000) || !(m >= 1 && m <= 12) || !(d >= 1) || d > daysInMonth(y, m)) return null;
        return { y: y, m: m, d: d };
    }

    function isoToJalali(iso) {
        var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso || "");
        return m ? toJalali(+m[1], +m[2], +m[3]) : null;
    }

    function jalaliToIso(j) { return formatIso(toGregorian(j.y, j.m, j.d)); }

    function todayIso() {
        var n = new Date();
        return n.getFullYear() + "-" + pad(n.getMonth() + 1, 2) + "-" + pad(n.getDate(), 2);
    }

    function addDaysIso(iso, days) {
        var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
        var d = new Date(+m[1], +m[2] - 1, +m[3] + days);
        return d.getFullYear() + "-" + pad(d.getMonth() + 1, 2) + "-" + pad(d.getDate(), 2);
    }

    // 0 = Saturday ... 6 = Friday (Persian week)
    function weekdayIndex(jy, jm, jd) {
        var g = toGregorian(jy, jm, jd);
        return (new Date(g.y, g.m - 1, g.d).getDay() + 1) % 7;
    }

    return {
        toJalali: toJalali, toGregorian: toGregorian, isLeap: isLeap, daysInMonth: daysInMonth, parseJalali: parseJalali,
        isoToJalali: isoToJalali, jalaliToIso: jalaliToIso, formatJalali: formatJalali, formatIso: formatIso, normalizeDigits: normalizeDigits,
        todayIso: todayIso, addDaysIso: addDaysIso, weekdayIndex: weekdayIndex
    };
});
