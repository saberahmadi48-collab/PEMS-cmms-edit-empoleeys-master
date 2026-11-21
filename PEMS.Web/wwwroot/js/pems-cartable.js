// PEMS Correspondence cartable shell: folder/tag tree behaviour, live clock, frozen columns, side detail pane, thread expansion,
// per-row operations, tag bulk menu, reminders. It only drives the existing server-rendered controls (checkboxes + the bulk form),
// so every rule (visibility, confidentiality, who may do what) is still enforced by the server. Pure helpers are exported for Node tests.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsCartable = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    var SIDE_KEY = "pems.cartable.sideCollapsed";
    var DENSITY_KEY = "pems.letter.compact";
    var COLUMNS_KEY = "pems.letter.index.hiddenColumns";
    var WIDE = 992;

    // ------------------------------------------------------------------ pure helpers
    function parseIdSet(raw) {
        try {
            var list = JSON.parse(raw);
            return Array.isArray(list) ? list.filter(function (x) { return typeof x === "number" || /^\d+$/.test(String(x)); }).map(String) : [];
        } catch (e) { return []; }
    }

    // cumulative inline-start offsets for the first `count` columns given their widths
    function frozenOffsets(widths, count) {
        var offsets = [], sum = 0;
        for (var i = 0; i < Math.min(count, widths.length); i++) { offsets.push(sum); sum += widths[i]; }
        return offsets;
    }

    // how many leading columns to freeze: 0 = none; N>0 = select + star + type + the first N data columns (subject, number, sender)
    function frozenColumnCount(setting) {
        var n = parseInt(setting, 10);
        if (!(n > 0)) return 0;
        return 3 + Math.min(n, 3);
    }

    function clockText(now, lang) {
        try {
            var fa = lang === "fa";
            var date = new Intl.DateTimeFormat(fa ? "fa-IR-u-ca-persian" : undefined, { weekday: "long", year: "numeric", month: "long", day: "numeric" }).format(now);
            var time = new Intl.DateTimeFormat(fa ? "fa-IR" : undefined, { hour: "2-digit", minute: "2-digit" }).format(now);
            return { date: date, time: time };
        } catch (e) { return { date: now.toDateString(), time: now.toTimeString().slice(0, 5) }; }
    }

    function defaultReminderValue(now) {
        var d = new Date(now.getTime());
        d.setDate(d.getDate() + 1);
        d.setHours(9, 0, 0, 0);
        var pad = function (n) { return (n < 10 ? "0" : "") + n; };
        return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) + "T" + pad(d.getHours()) + ":" + pad(d.getMinutes());
    }

    function read(key) { try { return window.localStorage.getItem(key); } catch (e) { return null; } }
    function write(key, value) { try { window.localStorage.setItem(key, value); } catch (e) { /* not remembered */ } }
    function remove(key) { try { window.localStorage.removeItem(key); } catch (e) { /* ignore */ } }

    var api = {
        cfg: null, t: function (k) { return k; },
        token: function () { var el = document.querySelector("#ctSettings input[name='__RequestVerificationToken']") || document.querySelector("input[name='__RequestVerificationToken']"); return el ? el.value : ""; },
        get: function (url) {
            return fetch(url, { headers: { "Accept": "application/json" }, credentials: "same-origin" }).then(function (r) {
                if (!r.ok) throw new Error(String(r.status));
                return r.json();
            });
        },
        post: function (url, body) {
            return fetch(url, {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/json", "Accept": "application/json", "RequestVerificationToken": api.token() },
                body: JSON.stringify(body || {})
            }).then(function (r) {
                if (!r.ok) throw new Error(String(r.status));
                return r.json();
            });
        },
        clearLocalViewState: function () { remove(DENSITY_KEY); remove(COLUMNS_KEY); }
    };

    // the server-rendered JSON block (i18n, urls, tags, settings); parsed as soon as this script runs so the settings script can use it too
    function loadConfig(doc) {
        var el = doc.getElementById("ctConfig");
        try { api.cfg = JSON.parse(el ? el.textContent : "{}"); } catch (e) { api.cfg = {}; }
        api.t = function (k) { return (api.cfg.i18n && api.cfg.i18n[k]) || k; };
        return api.cfg;
    }
    if (typeof document !== "undefined") loadConfig(document);

    // ------------------------------------------------------------------ DOM behaviour
    function init(doc) {
        var shell = doc.getElementById("ctShell");
        if (!shell) return null;

        var cfg = loadConfig(doc);

        var wide = function () { return window.innerWidth >= WIDE; };

        // ---- density from the saved settings (a per-browser choice made with D still wins)
        if (read(DENSITY_KEY) === null && shell.getAttribute("data-density") === "dense") doc.body.classList.add("lp-compact");

        // ---- clock
        var clock = doc.getElementById("ctClock");
        function tick() {
            if (!clock) return;
            var c = clockText(new Date(), shell.getAttribute("data-lang"));
            clock.textContent = "";
            clock.appendChild(window.PemsTemplate.fill(window.PemsTemplate.fragment("ct-clock", doc), { date: c.date + "  ", time: c.time }));
        }
        tick();
        if (clock) setInterval(tick, 30000);

        // ---- side panel (collapse on desktop, slide-in on small screens)
        var toggle = doc.getElementById("ctSideToggle");
        function applySide() {
            var collapsed = read(SIDE_KEY) === "1";
            shell.classList.toggle("ct-side-collapsed", wide() && collapsed);
            if (toggle) toggle.setAttribute("aria-expanded", wide() ? (collapsed ? "false" : "true") : (shell.classList.contains("ct-side-open") ? "true" : "false"));
        }
        if (toggle) toggle.addEventListener("click", function () {
            if (wide()) { write(SIDE_KEY, read(SIDE_KEY) === "1" ? "0" : "1"); applySide(); }
            else { shell.classList.toggle("ct-side-open"); applySide(); }
        });
        doc.addEventListener("click", function (e) {
            if (!wide() && shell.classList.contains("ct-side-open") && !e.target.closest("#ctSide") && !e.target.closest("#ctSideToggle")) { shell.classList.remove("ct-side-open"); applySide(); }
        });
        window.addEventListener("resize", applySide);
        applySide();

        // ---- tag tree: <pems-tree> in PEMS.UI opens, closes and remembers its branches (SCRUM-337 TreeView)

        // ---- frozen columns
        var table = doc.querySelector("table[data-grid-key='Letter']");
        function applyFrozen() {
            if (!table) return;
            var count = frozenColumnCount(shell.getAttribute("data-frozen"));
            var ths = Array.prototype.slice.call(table.querySelectorAll("thead th"));
            ths.forEach(function (th, i) {
                var rowsCells = [th].concat(Array.prototype.map.call(table.querySelectorAll("tbody tr.lp-row"), function (tr) { return tr.children[i]; }));
                rowsCells.forEach(function (c) { if (c) { c.classList.remove("ct-frozen"); c.style.insetInlineStart = ""; } });
            });
            if (!count) return;
            var visible = ths.filter(function (th) { return !th.classList.contains("pems-col-hidden"); }).slice(0, count);
            var offsets = frozenOffsets(visible.map(function (th) { return th.getBoundingClientRect().width; }), count);
            visible.forEach(function (th, k) {
                var idx = ths.indexOf(th);
                var cells = [th].concat(Array.prototype.map.call(table.querySelectorAll("tbody tr.lp-row"), function (tr) { return tr.children[idx]; }));
                cells.forEach(function (c) { if (c) { c.classList.add("ct-frozen"); c.style.insetInlineStart = offsets[k] + "px"; } });
            });
        }
        applyFrozen();
        window.addEventListener("resize", applyFrozen);
        if (table) table.addEventListener("mouseup", function () { setTimeout(applyFrozen, 50); });
        doc.addEventListener("change", function (e) { if (e.target && e.target.matches && e.target.matches("[data-col-toggle]")) setTimeout(applyFrozen, 0); });

        // ---- bulk form helpers
        var form = doc.getElementById("letterBulkForm");
        function rows() { return Array.prototype.slice.call(doc.querySelectorAll("table[data-grid-key='Letter'] tbody tr.lp-row:not(.ct-reply-row)")); }
        function fire(el) { el.dispatchEvent(new Event("change", { bubbles: true })); }
        function selectOnly(id) {
            rows().forEach(function (tr) {
                var c = tr.querySelector(".letter-select");
                if (!c) return;
                var want = String(tr.getAttribute("data-letter-id")) === String(id);
                if (c.checked !== want) { c.checked = want; fire(c); }
            });
        }
        function submitWith(op) {
            if (!form) return;
            var btn = form.querySelector("[data-row-submit='" + op + "']") || form.querySelector("[data-synth-submit='" + op + "']");
            if (!btn) return;
            if (typeof form.requestSubmit === "function") form.requestSubmit(btn); else btn.click();
        }
        if (form) {
            // synthetic submitters for tag / untag (the tag id travels in the hidden #ctBulkTagId input)
            ["tag", "untag"].forEach(function (op) {
                var b = window.PemsTemplate.clone("ct-synth-submit", doc);
                b.value = op; b.setAttribute("data-synth-submit", op);
                form.appendChild(b);
            });
            // trash confirmation follows the user's setting; purge always confirms
            var confirmTrash = shell.getAttribute("data-confirm-trash") === "1";
            // SCRUM-537: only the trash buttons; the shell carries data-confirm-trash as the setting flag and must never become a confirm target
            Array.prototype.forEach.call(doc.querySelectorAll("button[data-confirm-trash]"), function (b) { if (confirmTrash) b.setAttribute("data-pems-confirm", b.getAttribute("data-confirm-trash")); });
            // Row actions are standalone buttons, not form submit controls.  They use data-row-op,
            // so arm them from the document rather than searching the bulk-action form.
            Array.prototype.forEach.call(doc.querySelectorAll("button[data-row-op='trash']"), function (b) { if (confirmTrash) b.setAttribute("data-pems-confirm", api.t("confirmTrash")); });
            Array.prototype.forEach.call(doc.querySelectorAll("button[data-row-op='purge']"), function (b) { b.setAttribute("data-pems-confirm", api.t("confirmPurge")); });

            doc.addEventListener("click", function (e) {
                var tagBtn = e.target.closest ? e.target.closest("[data-bulk-tag]") : null;
                if (tagBtn) {
                    var hidden = doc.getElementById("ctBulkTagId");
                    if (hidden) hidden.value = tagBtn.getAttribute("data-bulk-tag");
                    submitWith(tagBtn.getAttribute("data-bulk-tag-op"));
                    return;
                }
                var rowBtn = e.target.closest ? e.target.closest("[data-row-op]") : null;
                if (rowBtn) {
                    e.preventDefault();
                    selectOnly(rowBtn.getAttribute("data-row-id"));
                    submitWith(rowBtn.getAttribute("data-row-op"));
                }
            });
        }
        // forms and buttons that ask first: [data-pems-confirm] (PEMS.UI pems-confirm.js)

        // ---- side detail pane (the real Details page in an iframe; actions inside it open the full page)
        var pane = doc.getElementById("ctDetail");
        var frame = doc.getElementById("ctDetailFrame");
        var titleEl = doc.getElementById("ctDetailTitle");
        var openFull = doc.getElementById("ctDetailOpen");
        function closePane() {
            shell.classList.remove("ct-detail-open");
            if (frame) frame.removeAttribute("src");
            rows().forEach(function (r) { r.classList.remove("ct-active"); });
            setTimeout(applyFrozen, 0);
        }
        function openPane(link) {
            if (!pane || !frame) return false;
            var tr = link.closest("tr");
            rows().forEach(function (r) { r.classList.remove("ct-active"); });
            if (tr) { tr.classList.add("ct-active"); tr.classList.remove("lp-unread"); }
            frame.src = link.getAttribute("data-preview-url");
            if (titleEl) titleEl.textContent = link.textContent.trim();
            if (openFull) openFull.href = link.href;
            shell.classList.add("ct-detail-open");
            setTimeout(applyFrozen, 0);
            return true;
        }
        var closeBtn = doc.getElementById("ctDetailClose");
        if (closeBtn) closeBtn.addEventListener("click", closePane);
        doc.addEventListener("click", function (e) {
            var link = e.target.closest ? e.target.closest("a.lp-subject[data-preview-url]") : null;
            if (!link || !wide() || shell.getAttribute("data-preview") !== "1") return;
            if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
            if (openPane(link)) e.preventDefault();
        });
        doc.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && shell.classList.contains("ct-detail-open") && !doc.querySelector(".modal.show, .dropdown-menu.show, .offcanvas.show") && !(e.target && /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName))) closePane();
        });

        // ---- thread rows (expand a conversation under its row)
        doc.addEventListener("click", function (e) {
            var chip = e.target.closest ? e.target.closest("[data-thread-toggle]") : null;
            if (!chip) return;
            e.preventDefault();
            var id = chip.getAttribute("data-thread-toggle");
            var tr = chip.closest("tr");
            var open = chip.getAttribute("aria-expanded") === "true";
            Array.prototype.forEach.call(doc.querySelectorAll("tr[data-thread-for='" + id + "']"), function (n) { n.remove(); });
            if (open) { chip.setAttribute("aria-expanded", "false"); return; }
            chip.setAttribute("aria-expanded", "true");
            var cols = table ? table.querySelectorAll("thead th").length : 14;
            // SCRUM-337: a skeleton row while the replies load
            var wait = window.PemsTemplate.clone("ct-thread-wait", doc);
            wait.setAttribute("data-thread-for", id);
            var waitTd = wait.firstElementChild; waitTd.colSpan = cols;
            window.PemsSkeleton.show(waitTd, "table", { rows: 2, cols: 3 });
            tr.parentNode.insertBefore(wait, tr.nextSibling);
            api.get(cfg.urls.threadMembers + "?id=" + encodeURIComponent(id)).then(function (members) {
                wait.remove();
                var anchor = tr;
                members.forEach(function (m) {
                    var row = window.PemsTemplate.fill(window.PemsTemplate.clone("ct-reply-row", doc), {
                        subject: { text: m.subject, attrs: { href: m.url }, addClass: m.isCurrent ? "fw-bold" : "" },
                        meta: " — " + m.letterNumber + " · " + (m.senderName || "") + " · " + m.date + " · " + m.status
                    });
                    row.setAttribute("data-thread-for", id);
                    row.style.setProperty("--depth", String(m.depth + 1));
                    row.firstElementChild.colSpan = cols;
                    anchor.parentNode.insertBefore(row, anchor.nextSibling);
                    anchor = row;
                });
            }).catch(function () { wait.remove(); chip.setAttribute("aria-expanded", "false"); });
        });

        // ---- "#" moves the cursor row to the trash (Gmail-style)
        doc.addEventListener("keydown", function (e) {
            if (e.key !== "#" || e.ctrlKey || e.metaKey || e.altKey) return;
            var t = e.target;
            if (t && (/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName) || t.isContentEditable)) return;
            if (doc.querySelector(".modal.show, .offcanvas.show")) return;
            var cur = doc.querySelector("tr.lp-row.lp-cursor [data-row-op='trash']");
            if (cur) { e.preventDefault(); cur.click(); }
        });

        // ---- personal reminder modal
        var remModal = doc.getElementById("ctReminderModal");
        if (remModal && window.bootstrap) {
            var remLetter = null;
            var msg = doc.getElementById("ctReminderMsg");
            // SCRUM-543: the reminder is a <pems-date time="true">; on a Persian page its ISO value lives in a hidden input, so read and write it through PemsDate
            var remAt = doc.getElementById("ctReminderAt");
            var setReminderAt = function (iso) { if (window.PemsDate && window.PemsDate.setValue) window.PemsDate.setValue(remAt, iso); else remAt.value = iso; };
            var reminderAt = function () { return window.PemsDate && window.PemsDate.value ? window.PemsDate.value(remAt) : remAt.value; };
            doc.addEventListener("click", function (e) {
                var b = e.target.closest ? e.target.closest("[data-reminder-letter]") : null;
                if (!b) return;
                remLetter = b.getAttribute("data-reminder-letter");
                doc.getElementById("ctReminderLetter").textContent = b.getAttribute("data-reminder-title") || "";
                setReminderAt(defaultReminderValue(new Date()));
                doc.getElementById("ctReminderNote").value = "";
                msg.textContent = ""; msg.className = "ct-msg";
                window.bootstrap.Modal.getOrCreateInstance(remModal).show();
            });
            doc.getElementById("ctReminderSave").addEventListener("click", function () {
                var at = reminderAt();
                if (!at) { msg.textContent = api.t("pickDate"); msg.className = "ct-msg err"; return; }
                api.post(cfg.urls.reminderAdd, { letterId: parseInt(remLetter, 10), remindAt: at, note: doc.getElementById("ctReminderNote").value })
                    .then(function (r) {
                        if (r.ok) { msg.textContent = api.t("reminderAdded"); msg.className = "ct-msg ok"; setTimeout(function () { window.bootstrap.Modal.getOrCreateInstance(remModal).hide(); }, 700); }
                        else { msg.textContent = r.error || api.t("failed"); msg.className = "ct-msg err"; }
                    }).catch(function () { msg.textContent = api.t("failed"); msg.className = "ct-msg err"; });
            });
        }

        return { closePane: closePane, applyFrozen: applyFrozen };
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", function () { init(document); });
        else init(document);
    }

    api.parseIdSet = parseIdSet;
    api.frozenOffsets = frozenOffsets;
    api.frozenColumnCount = frozenColumnCount;
    api.clockText = clockText;
    api.defaultReminderValue = defaultReminderValue;
    api.init = init;
    return api;
});
