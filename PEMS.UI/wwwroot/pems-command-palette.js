// PEMS command palette (SCRUM-64). Ctrl+K / Cmd+K (or a click in the top-bar search box) opens one dialog that finds:
//   - menus: read from the rendered sidebar (#pemsSidebar), which the server has already filtered by permission;
//   - actions: GET data-actions-url, each action already checked by the server against the rule of its page;
//   - records: GET data-search-url?q=, only modules whose access rule the user passes, each with its row scope.
// Nothing here decides access: the browser only shows what the server returned or rendered for this user.
// Texts come from data-* attributes of the <script> tag (both resx files). Offline: no library, no external call.
// Pure helpers (normalize, matches, filterLocal) are exported for Node tests; the DOM part only runs in a browser.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsCommandPalette = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var MAX_MENUS = 8, MAX_ACTIONS = 6, MIN_RECORD_CHARS = 2, DEBOUNCE_MS = 220;

    // Same normalisation as the sidebar search: Arabic/Persian letter forms, ZWNJ and tatweel, case, spaces.
    function normalize(text) {
        return String(text == null ? "" : text)
            .replace(/ي|ى/g, "ی")
            .replace(/ك/g, "ک")
            .replace(/[‌​‍ـ]/g, "")
            .replace(/\s+/g, " ")
            .trim()
            .toLowerCase();
    }

    // Every word of the query must appear in the text (spaces inside Persian compounds are ignored).
    function matches(text, query) {
        var hay = normalize(text).replace(/ /g, "");
        var words = normalize(query).split(" ").filter(Boolean);
        return words.length > 0 && words.every(function (w) { return hay.indexOf(w) !== -1; });
    }

    // Items whose label (or section) matches, label-start matches first; an empty query keeps the first `max`.
    function filterLocal(items, query, max) {
        var q = normalize(query);
        if (!q) return items.slice(0, max);
        var hits = items.filter(function (it) { return matches(it.label + " " + (it.sub || ""), q); });
        hits.sort(function (a, b) {
            var sa = normalize(a.label).indexOf(q) === 0 ? 0 : 1, sb = normalize(b.label).indexOf(q) === 0 ? 0 : 1;
            return sa - sb;
        });
        return hits.slice(0, max);
    }

    function format(template, value) { return String(template || "").split("{0}").join(String(value)); }

    // the markup is the page's registered template (script-templates, set shell); this file only fills it
    function tpl(doc, name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name, doc), values || {}); }

    // Menu links of the sidebar the server rendered for this user (hidden-by-filter items included, real links only).
    function readMenus(doc) {
        var nav = doc.getElementById("pemsSidebar");
        if (!nav) return [];
        var out = [], seen = {};
        Array.prototype.forEach.call(nav.querySelectorAll("a.nav-link[href]"), function (a) {
            var href = a.getAttribute("href");
            if (!href || href === "#" || href.indexOf("javascript:") === 0 || seen[href]) return;
            var labelEl = a.querySelector(".nav-label");
            var label = (labelEl || a).textContent.replace(/\s+/g, " ").trim();
            if (!label) return;
            var section = "", node = a;
            while (node && node.parentElement !== nav) node = node.parentElement;
            for (var p = node && node.previousElementSibling; p; p = p.previousElementSibling) {
                if (p.classList.contains("nav-section-title")) { section = p.textContent.replace(/\s+/g, " ").trim(); break; }
            }
            var icon = a.querySelector("i.bi");
            seen[href] = true;
            out.push({ label: label, sub: section, url: href, icon: icon ? (icon.className.match(/bi-[\w-]+/) || ["bi-list"])[0] : "bi-list" });
        });
        return out;
    }

    function init(doc, cfg) {
        if (doc.querySelector(".pems-cp-backdrop")) return null;
        var t = cfg.texts || {};
        var uid = "pems-cp-" + Math.random().toString(36).slice(2, 8);

        // ---- markup (built once, hidden until opened) ----
        var backdrop = tpl(doc, "cp-palette", { esc: "Esc", keys: t.keys || "" });
        var dialog = backdrop.querySelector(".pems-cp");
        dialog.setAttribute("aria-label", t.title || "Command palette");
        var input = backdrop.querySelector(".pems-cp-input");
        input.placeholder = t.placeholder || "";
        input.setAttribute("aria-label", t.placeholder || t.title || "Search");
        input.setAttribute("aria-controls", uid + "-list");
        var list = backdrop.querySelector(".pems-cp-list");
        list.id = uid + "-list";
        list.setAttribute("aria-label", t.title || "Results");
        var status = backdrop.querySelector(".pems-cp-status");
        doc.body.appendChild(backdrop);

        // ---- state ----
        var menus = null, actions = null, actionsLoading = null;
        var records = { query: "", groups: [], state: "idle" };   // idle | loading | done | error
        var options = [], active = -1, timer = null, controller = null, opener = null, seq = 0, moved = false;

        function loadActions() {
            if (actions || actionsLoading || !cfg.actionsUrl) return;
            var url = cfg.actionsUrl + (cfg.actionsUrl.indexOf("?") < 0 ? "?" : "&") + "returnUrl=" +
                encodeURIComponent(location.pathname + location.search);
            actionsLoading = fetch(url, { headers: { Accept: "application/json" }, credentials: "same-origin" })
                .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                .then(function (data) { actions = (data.items || []).map(function (a) { return { label: a.label, url: a.url, icon: a.icon }; }); })
                .catch(function () { actions = []; })
                .then(function () { actionsLoading = null; render(); });
        }

        function searchRecords(q) {
            clearTimeout(timer);
            if (controller) { controller.abort(); controller = null; }
            if (normalize(q).length < MIN_RECORD_CHARS || !cfg.searchUrl) {
                records = { query: q, groups: [], state: "idle" };
                return;
            }
            records = { query: q, groups: records.query && q.indexOf(records.query) === 0 ? records.groups : [], state: "loading" };
            var mine = ++seq;
            timer = setTimeout(function () {
                controller = typeof AbortController === "function" ? new AbortController() : null;
                fetch(cfg.searchUrl + (cfg.searchUrl.indexOf("?") < 0 ? "?" : "&") + "q=" + encodeURIComponent(q), {
                    headers: { Accept: "application/json" }, credentials: "same-origin", signal: controller ? controller.signal : undefined
                })
                    .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                    .then(function (data) { if (mine === seq) { records = { query: q, groups: data.groups || [], state: "done" }; render(); } })
                    .catch(function (e) { if (mine === seq && (!e || e.name !== "AbortError")) { records = { query: q, groups: [], state: "error" }; render(); } });
            }, DEBOUNCE_MS);
        }

        // ---- rendering ----
        function addGroup(title, items, kind) {
            if (!items.length) return;
            var gid = uid + "-g" + list.children.length;
            var group = tpl(doc, "cp-group", { title: title ? title : null });
            if (title) {
                var h = group.querySelector(".pems-cp-group-title");
                h.id = gid;
                group.setAttribute("aria-labelledby", gid);
            }
            items.forEach(function (it) {
                var a = tpl(doc, "cp-option", {
                    root: { addClass: "pems-cp-" + kind, attrs: { href: it.url } },
                    icon: { addClass: it.icon || "bi-arrow-right-short" }, label: it.label, sub: it.sub ? it.sub : null
                });
                a.id = uid + "-o" + options.length;
                a.addEventListener("mousemove", function () { moved = true; setActive(options.indexOf(a), false); });
                options.push(a);
                group.appendChild(a);
            });
            list.appendChild(group);
        }

        function render() {
            if (backdrop.hidden) return;
            var q = input.value;
            // keep the highlighted entry only when the user moved to it; otherwise start on the first one
            var keep = moved && active >= 0 && options[active] ? options[active].getAttribute("href") : null;
            list.textContent = "";
            options = [];

            if (menus === null) menus = readMenus(doc);
            addGroup(t.actions || "Actions", filterLocal(actions || [], q, q ? MAX_ACTIONS : MAX_ACTIONS), "action");
            if (normalize(q)) addGroup(t.menus || "Menus", filterLocal(menus, q, MAX_MENUS), "menu");
            records.groups.forEach(function (g) {
                addGroup(g.title, g.items.map(function (x) { return { label: x.label, sub: x.sub, url: x.url, icon: x.icon }; }), "record");
            });
            if (normalize(q).length >= MIN_RECORD_CHARS && cfg.allUrl) {
                addGroup("", [{ label: format(t.showAll, q.trim()), url: cfg.allUrl + "?q=" + encodeURIComponent(q.trim()), icon: "bi-search" }], "all");
            }

            var msg = "";
            if (records.state === "loading") msg = t.loading || "";
            else if (records.state === "error") msg = t.error || "";
            else if (normalize(q) && normalize(q).length < MIN_RECORD_CHARS) msg = t.minChars || "";
            else if (normalize(q) && options.length <= 1) msg = t.empty || "";
            else if (records.state === "done") msg = format(t.count, records.groups.reduce(function (n, g) { return n + g.items.length; }, 0));
            status.textContent = msg;
            dialog.setAttribute("aria-busy", records.state === "loading" ? "true" : "false");

            var idx = keep ? options.map(function (o) { return o.getAttribute("href"); }).indexOf(keep) : -1;
            setActive(idx >= 0 ? idx : (options.length ? 0 : -1), false);
        }

        function setActive(i, scroll) {
            if (active >= 0 && options[active]) {
                options[active].classList.remove("active");
                options[active].setAttribute("aria-selected", "false");
            }
            active = i;
            if (i >= 0 && options[i]) {
                options[i].classList.add("active");
                options[i].setAttribute("aria-selected", "true");
                input.setAttribute("aria-activedescendant", options[i].id);
                if (scroll !== false && options[i].scrollIntoView) options[i].scrollIntoView({ block: "nearest" });
            } else {
                input.removeAttribute("aria-activedescendant");
            }
        }

        // ---- open / close ----
        function open(initial) {
            if (!backdrop.hidden) { input.focus(); input.select(); return; }
            opener = doc.activeElement;
            backdrop.hidden = false;
            doc.documentElement.classList.add("pems-cp-open");
            input.value = initial || "";
            records = { query: "", groups: [], state: "idle" };
            menus = null;
            loadActions();
            searchRecords(input.value);
            render();
            input.focus();
            input.select();
        }

        function close() {
            if (backdrop.hidden) return;
            backdrop.hidden = true;
            doc.documentElement.classList.remove("pems-cp-open");
            clearTimeout(timer);
            if (controller) { controller.abort(); controller = null; }
            if (opener && opener.focus && opener !== doc.body) { try { opener.focus({ preventScroll: true }); } catch (e) { /* element gone */ } }
            opener = null;
        }

        function go(i, newTab) {
            var a = options[i];
            if (!a) return;
            var href = a.getAttribute("href");
            if (newTab) { window.open(href, "_blank", "noopener"); return; }
            close();
            location.assign(href);
        }

        input.addEventListener("input", function () { moved = false; searchRecords(input.value); render(); });
        input.addEventListener("keydown", function (e) {
            if (e.key === "ArrowDown" || e.key === "ArrowUp") {
                e.preventDefault();
                if (!options.length) return;
                var n = options.length;
                moved = true;
                setActive(((active < 0 ? -1 : active) + (e.key === "ArrowDown" ? 1 : -1) + n) % n);
            } else if (e.key === "Home" && options.length && e.ctrlKey) {
                e.preventDefault(); setActive(0);
            } else if (e.key === "End" && options.length && e.ctrlKey) {
                e.preventDefault(); setActive(options.length - 1);
            } else if (e.key === "Enter") {
                e.preventDefault();
                go(active, e.ctrlKey || e.metaKey);
            } else if (e.key === "Escape") {
                e.preventDefault();
                close();
            } else if (e.key === "Tab") {
                e.preventDefault();   // focus stays in the dialog: the input is its only tab stop
            }
        });
        backdrop.addEventListener("mousedown", function (e) { if (e.target === backdrop) close(); });
        list.addEventListener("click", function (e) {
            var a = e.target.closest ? e.target.closest("a.pems-cp-option") : null;
            if (!a || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;   // let the browser open new tabs
            e.preventDefault();
            go(options.indexOf(a), false);
        });

        doc.addEventListener("keydown", function (e) {
            if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && (e.key === "k" || e.key === "K" || e.code === "KeyK")) {
                e.preventDefault();
                if (backdrop.hidden) open(""); else close();
            }
        });

        // The top-bar search box opens the palette with what was typed; without JavaScript it stays a normal form.
        Array.prototype.forEach.call(doc.querySelectorAll("[data-pems-command-palette-trigger]"), function (box) {
            function fromBox(e) {
                if (e) e.preventDefault();
                var v = box.value || "";
                box.blur();
                open(v);
            }
            box.addEventListener("focus", function () { fromBox(); });
            box.addEventListener("keydown", function (e) { if (e.key === "Enter") fromBox(e); });
        });
        Array.prototype.forEach.call(doc.querySelectorAll("[data-pems-command-palette-open]"), function (btn) {
            btn.addEventListener("click", function (e) { e.preventDefault(); open(""); });
        });

        return { open: open, close: close, input: input, list: list };
    }

    if (typeof document !== "undefined") {
        var script = document.currentScript;
        var ds = script ? script.dataset : {};
        var start = function () {
            init(document, {
                searchUrl: ds.searchUrl, actionsUrl: ds.actionsUrl, allUrl: ds.allUrl,
                texts: {
                    title: ds.title, placeholder: ds.placeholder, menus: ds.menus, actions: ds.actions, empty: ds.empty,
                    loading: ds.loading, error: ds.error, keys: ds.keys, minChars: ds.minChars, showAll: ds.showAll, count: ds.count
                }
            });
        };
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
        else start();
    }

    return { normalize: normalize, matches: matches, filterLocal: filterLocal, readMenus: readMenus, init: init };
});
