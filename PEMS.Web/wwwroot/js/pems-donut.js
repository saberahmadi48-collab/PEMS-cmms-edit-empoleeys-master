// PEMS donut chart: an SVG ring whose slices and legend highlight each other, with the hovered slice's value in the centre.
// The server renders the legend (label + value per <li data-value>), which is also the accessible/no-JS version of the data;
// this file draws the ring from that legend and wires the hover/focus link. Colours come from PemsChartColors (theme aware).
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsDonut = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var C = 50, OUTER = 46, INNER = 29, GAP_DEG = 1.4;
    var NS = "http://www.w3.org/2000/svg";
    var FALLBACK = ["#94a3b8", "#f59e0b", "#22c55e", "#ef4444", "#3b82f6", "#a855f7", "#14b8a6"];

    function fmt(n) { return (Math.round(n * 1000) / 1000).toString(); }

    // clockwise from 12 o'clock, degrees -> point on a circle of radius r
    function point(r, deg) {
        var rad = (deg - 90) * Math.PI / 180;
        return [C + r * Math.cos(rad), C + r * Math.sin(rad)];
    }

    // Slices in order; zero/negative values are skipped (they would be invisible slivers) but keep their original index.
    function layout(values) {
        var total = 0;
        values.forEach(function (v) { if (v > 0) total += v; });
        if (total <= 0) return { total: 0, slices: [] };
        var angle = 0, slices = [];
        values.forEach(function (v, i) {
            if (!(v > 0)) return;
            var sweep = v / total * 360;
            slices.push({ index: i, start: angle, end: angle + sweep, fraction: v / total });
            angle += sweep;
        });
        return { total: total, slices: slices };
    }

    // One ring segment; a 100% slice cannot be one arc (start == end), so it is drawn as two full circles with even-odd fill.
    function arcPath(rOuter, rInner, start, end) {
        var sweep = end - start;
        if (sweep >= 359.999) {
            return "M" + fmt(C) + " " + fmt(C - rOuter) + "A" + rOuter + " " + rOuter + " 0 1 1 " + fmt(C) + " " + fmt(C + rOuter)
                + "A" + rOuter + " " + rOuter + " 0 1 1 " + fmt(C) + " " + fmt(C - rOuter) + "Z"
                + "M" + fmt(C) + " " + fmt(C - rInner) + "A" + rInner + " " + rInner + " 0 1 0 " + fmt(C) + " " + fmt(C + rInner)
                + "A" + rInner + " " + rInner + " 0 1 0 " + fmt(C) + " " + fmt(C - rInner) + "Z";
        }
        var large = sweep > 180 ? 1 : 0;
        var p1 = point(rOuter, start), p2 = point(rOuter, end), p3 = point(rInner, end), p4 = point(rInner, start);
        return "M" + fmt(p1[0]) + " " + fmt(p1[1]) + "A" + rOuter + " " + rOuter + " 0 " + large + " 1 " + fmt(p2[0]) + " " + fmt(p2[1])
            + "L" + fmt(p3[0]) + " " + fmt(p3[1]) + "A" + rInner + " " + rInner + " 0 " + large + " 0 " + fmt(p4[0]) + " " + fmt(p4[1]) + "Z";
    }

    // a visible gap between neighbouring slices, but never so large that a thin slice disappears
    function trimmed(slice, count) {
        var sweep = slice.end - slice.start;
        if (count < 2 || sweep <= GAP_DEG * 3) return [slice.start, slice.end];
        return [slice.start + GAP_DEG / 2, slice.end - GAP_DEG / 2];
    }

    function percentText(fraction, locale) {
        try { return new Intl.NumberFormat(locale || undefined, { style: "percent", maximumFractionDigits: 1 }).format(fraction); }
        catch (e) { return (Math.round(fraction * 1000) / 10) + "%"; }
    }

    function numberText(n, locale) {
        try { return new Intl.NumberFormat(locale || undefined).format(n); } catch (e) { return String(n); }
    }

    function init(el, doc) {
        doc = doc || document;
        var svgRoot = el.querySelector("svg"), group = el.querySelector(".pdn-slices");
        var valueEl = el.querySelector(".pdn-center-value"), labelEl = el.querySelector(".pdn-center-label");
        var items = Array.prototype.slice.call(el.querySelectorAll(".pdn-item"));
        if (!svgRoot || !group || !items.length) return null;

        var locale = doc.documentElement.lang || undefined;
        var palette = (window.PemsChartColors && window.PemsChartColors.categorical) || FALLBACK;
        var values = items.map(function (li) { return parseFloat(li.getAttribute("data-value")) || 0; });
        var plan = layout(values);
        var defaultLabel = el.getAttribute("data-default-label") || "";
        var paths = {};

        items.forEach(function (li, i) {
            var marker = li.querySelector(".pdn-marker");
            if (marker) marker.style.background = palette[i % palette.length];
            var valueText = li.querySelector(".pdn-value");
            if (valueText) valueText.textContent = numberText(values[i], locale);   // same digits as the centre and the percentage
            var pct = li.querySelector(".pdn-pct");
            var slice = plan.slices.filter(function (s) { return s.index === i; })[0];
            if (pct) pct.textContent = slice ? percentText(slice.fraction, locale) : percentText(0, locale);
            li.setAttribute("aria-label", (li.getAttribute("data-label") || "") + ": " + numberText(values[i], locale) + (slice ? " (" + percentText(slice.fraction, locale) + ")" : ""));
        });

        plan.slices.forEach(function (s) {
            var range = trimmed(s, plan.slices.length);
            var path = doc.createElementNS(NS, "path");
            path.setAttribute("d", arcPath(OUTER, INNER, range[0], range[1]));
            path.setAttribute("class", "pdn-slice");
            path.setAttribute("fill", palette[s.index % palette.length]);
            path.setAttribute("fill-rule", "evenodd");
            var title = doc.createElementNS(NS, "title");
            title.textContent = (items[s.index].getAttribute("data-label") || "") + ": " + numberText(values[s.index], locale) + " (" + percentText(s.fraction, locale) + ")";
            path.appendChild(title);
            group.appendChild(path);
            paths[s.index] = path;
            path.addEventListener("pointerenter", function () { setHover(s.index); });
            path.addEventListener("pointerleave", function () { setHover(null); });
        });

        function setHover(index) {
            var hovering = index !== null && index !== undefined;
            el.classList.toggle("has-hover", hovering);
            Object.keys(paths).forEach(function (k) { paths[k].classList.toggle("is-hover", hovering && +k === index); });
            items.forEach(function (li, i) { li.classList.toggle("is-hover", hovering && i === index); });
            if (hovering) {
                valueEl.textContent = numberText(values[index], locale);
                labelEl.textContent = items[index].getAttribute("data-label") || "";
            } else {
                valueEl.textContent = numberText(plan.total, locale);
                labelEl.textContent = defaultLabel;
            }
        }

        items.forEach(function (li, i) {
            li.addEventListener("pointerenter", function () { setHover(i); });
            li.addEventListener("pointerleave", function () { setHover(null); });
            li.addEventListener("focus", function () { setHover(i); });
            li.addEventListener("blur", function () { setHover(null); });
        });

        setHover(null);
        el.classList.add("is-ready");
        return { setHover: setHover, plan: plan };
    }

    function initAll(doc) {
        doc = doc || document;
        Array.prototype.forEach.call(doc.querySelectorAll(".pdn"), function (el) { init(el, doc); });
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", function () { initAll(document); });
        else initAll(document);
    }

    return { layout: layout, arcPath: arcPath, trimmed: trimmed, percentText: percentText, init: init, initAll: initAll };
});
