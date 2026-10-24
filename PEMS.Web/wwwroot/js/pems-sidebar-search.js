// PEMS sidebar menu search. Adds a search box to the top of #pemsSidebar (both the static and the data-driven
// sidebar share that id and the .nav-section-title / .collapse / .nav-link structure) and filters the existing items
// client-side: sections with a match are expanded, everything else is hidden, and clearing the box restores exactly the
// expanded/collapsed state the user had. The markup of the sidebar partials is not touched.
// Pure helpers (normalize/matches) are exported for Node tests; the DOM part only runs in a browser.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsSidebarSearch = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var HIDDEN = "pems-sb-hidden";

    // Persian text is typed with Arabic or Persian letter forms and sometimes with ZWNJ; compare on a normalised form
    function normalize(text) {
        return String(text == null ? "" : text)
            .replace(/ي|ى/g, "ی")
            .replace(/ك/g, "ک")
            .replace(/[‌​‍ـ]/g, "")
            .replace(/\s+/g, " ")
            .trim()
            .toLowerCase();
    }

    // q must already be normalised. Spaces are ignored on both sides: Persian compounds are written both as
    // "ابزار دقیق" and "ابزاردقیق", and a user should find the item either way.
    function matches(label, q) {
        var needle = q.replace(/ /g, "");
        return needle !== "" && normalize(label).replace(/ /g, "").indexOf(needle) !== -1;
    }

    function format(template, n) {
        return String(template || "").split("{0}").join(String(n));
    }

    function init(doc, cfg) {
        var nav = doc.getElementById("pemsSidebar");
        if (!nav || nav.querySelector(".pems-sb-search")) return null;

        var wrap = doc.createElement("div");
        wrap.className = "pems-sb-search";
        var icon = doc.createElement("i");
        icon.className = "bi bi-search";
        icon.setAttribute("aria-hidden", "true");
        var input = doc.createElement("input");
        input.type = "search";
        input.className = "pems-sb-search-input";
        input.placeholder = cfg.placeholder || "";
        input.setAttribute("aria-label", cfg.placeholder || "Search menu");
        input.setAttribute("autocomplete", "off");
        input.setAttribute("aria-controls", "pemsSidebar");
        wrap.appendChild(icon);
        wrap.appendChild(input);

        var status = doc.createElement("div");
        status.className = "pems-sb-status";
        status.setAttribute("role", "status");
        status.setAttribute("aria-live", "polite");

        nav.insertBefore(wrap, nav.firstChild);
        nav.insertBefore(status, wrap.nextElementSibling);

        var saved = null;   // expanded state of every section before the first keystroke

        function units() {
            var out = [];
            Array.prototype.forEach.call(nav.children, function (el) {
                if (el === wrap || el === status) return;
                if (el.classList.contains("nav-section-title")) {
                    var body = el.nextElementSibling;
                    out.push({ type: "section", title: el, body: body && body.classList.contains("collapse") ? body : null });
                } else if (el.classList.contains("nav-link")) {
                    out.push({ type: "link", el: el });
                }
            });
            return out;
        }

        function label(el) {
            var l = el.querySelector(".nav-label");
            return (l || el).textContent;
        }

        function hide(el, hidden) { el.classList.toggle(HIDDEN, !!hidden); }

        function expand(u, on) {
            if (!u.body) return;
            u.body.classList.toggle("show", on);
            u.title.setAttribute("aria-expanded", on ? "true" : "false");
            var chevron = u.title.querySelector(".nav-section-chevron");
            if (chevron) chevron.classList.toggle("rotated", on);
        }

        function restore() {
            Array.prototype.forEach.call(nav.querySelectorAll("." + HIDDEN), function (el) { el.classList.remove(HIDDEN); });
            if (saved) saved.forEach(function (s) { expand(s.u, s.shown); });
            saved = null;
            status.textContent = "";
        }

        function filter(raw) {
            var q = normalize(raw);
            if (!q) { restore(); return 0; }

            var all = units();
            if (!saved) saved = all.filter(function (u) { return u.type === "section" && u.body; })
                .map(function (u) { return { u: u, shown: u.body.classList.contains("show") }; });

            var total = 0;
            all.forEach(function (u) {
                if (u.type === "link") {
                    var visible = matches(label(u.el), q);
                    hide(u.el, !visible);
                    if (visible) total++;
                    return;
                }

                var titleMatch = matches(u.title.textContent, q);
                var anyVisible = false;
                if (u.body) {
                    Array.prototype.forEach.call(u.body.querySelectorAll("a.nav-link"), function (l) {
                        var vis = titleMatch || matches(label(l), q);
                        hide(l, !vis);
                        if (vis) { anyVisible = true; total++; }
                    });

                    // a sub-heading is shown only while at least one item below it (up to the next sub-heading) is shown
                    var head = null, headHasVisible = false;
                    Array.prototype.forEach.call(u.body.children, function (c) {
                        if (c.classList.contains("nav-subheading")) {
                            if (head) hide(head, !headHasVisible);
                            head = c; headHasVisible = false;
                        } else if (c.matches("a.nav-link") && !c.classList.contains(HIDDEN)) {
                            headHasVisible = true;
                        }
                    });
                    if (head) hide(head, !headHasVisible);
                }

                var show = titleMatch || anyVisible;
                hide(u.title, !show);
                if (u.body) hide(u.body, !show);
                if (show) expand(u, true);
            });

            status.textContent = total > 0 ? format(cfg.results, total) : (cfg.empty || "");
            return total;
        }

        function firstVisibleLink() {
            return nav.querySelector("a.nav-link:not(." + HIDDEN + ")");
        }

        var timer = null;
        input.addEventListener("input", function () {
            clearTimeout(timer);
            timer = setTimeout(function () { filter(input.value); }, 80);
        });
        input.addEventListener("keydown", function (e) {
            if (e.key === "Escape") {
                input.value = "";
                filter("");
            } else if (e.key === "Enter") {
                var first = firstVisibleLink();
                if (input.value.trim() && first) { e.preventDefault(); first.click(); }
            } else if (e.key === "ArrowDown") {
                var link = firstVisibleLink();
                if (link) { e.preventDefault(); link.focus(); }
            }
        });

        return { input: input, status: status, filter: filter };
    }

    // auto-start in the browser: the script is loaded at the end of <body>, so the sidebar already exists
    if (typeof document !== "undefined" && document.currentScript) {
        var ds = document.currentScript.dataset || {};
        init(document, { placeholder: ds.placeholder, results: ds.results, empty: ds.empty });
    }

    return { normalize: normalize, matches: matches, init: init };
});
