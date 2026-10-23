// DCC "Register incoming correspondence" page. The server owns every rule (PEMS.Application IncomingCorrespondenceRules + IIncomingCorrespondenceService);
// this file only collects the form, shows the scan preview / party thread, and renders the server's field errors next to the fields.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory(require("./pems-jalali.js"));
    else root.PemsDccRegister = factory(root.PemsJalali);
})(typeof self !== "undefined" ? self : this, function (Jalali) {
    "use strict";

    var MONTHS = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
    var WEEKDAYS = ["ش", "ی", "د", "س", "چ", "پ", "ج"];
    var DEBOUNCE_MS = 220;

    function debounce(fn, ms) {
        var t;
        return function () { var a = arguments, self = this; clearTimeout(t); t = setTimeout(function () { fn.apply(self, a); }, ms); };
    }

    function formatBytes(n) {
        if (n < 1024) return n + " B";
        if (n < 1024 * 1024) return (n / 1024).toFixed(0) + " KB";
        return (n / 1024 / 1024).toFixed(1) + " MB";
    }

    function esc(s) {
        return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]; });
    }

    function fileKind(name) {
        var ext = (/\.([^.]+)$/.exec(name || "") || [, ""])[1].toLowerCase();
        if (ext === "pdf") return "pdf";
        if (["png", "jpg", "jpeg", "gif", "webp"].indexOf(ext) >= 0) return "image";
        return "other";
    }

    // Due-date preset: N days after the received date (falls back to today)
    function presetDue(receivedIso, days) {
        return Jalali.addDaysIso(receivedIso || Jalali.todayIso(), days);
    }

    function init(doc) {
        var rootEl = doc.getElementById("dcr");
        if (!rootEl) return null;

        var i18n = {};
        try { i18n = JSON.parse(doc.getElementById("dcrI18n").textContent || "{}"); } catch (e) { /* labels fall back to keys */ }
        function T(key) { return i18n[key] || key; }

        var ds = rootEl.dataset;
        var form = doc.getElementById("dcrForm");
        var $ = function (id) { return doc.getElementById(id); };

        var state = {
            party: null,        // {id,name,partyType,isProvisional,contracts}
            newPartyName: "",
            files: [],          // {uid,file,role}
            links: [],          // {linkType,targetId,number,title}
            cc: [],             // {employeeId,name}
            owner: null,        // {employeeId,name}
            confirmDuplicate: false,
            submitting: false,
            dirty: false,
            uid: 0
        };

        function getJson(url, params) {
            var q = Object.keys(params || {}).filter(function (k) { return params[k] !== undefined && params[k] !== null && params[k] !== ""; })
                .map(function (k) { return encodeURIComponent(k) + "=" + encodeURIComponent(params[k]); }).join("&");
            return fetch(url + (q ? (url.indexOf("?") >= 0 ? "&" : "?") + q : ""), { credentials: "same-origin", headers: { "Accept": "application/json" } })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); });
        }

        // ------------------------------------------------------------ combobox (input + listbox, keyboard: arrows/Enter/Esc)
        function combobox(opts) {
            var input = opts.input, list = opts.list, items = [], active = -1, seq = 0;

            function close() { list.hidden = true; input.setAttribute("aria-expanded", "false"); input.removeAttribute("aria-activedescendant"); active = -1; }
            function open() { list.hidden = false; input.setAttribute("aria-expanded", "true"); }
            function setActive(i) {
                var nodes = list.querySelectorAll("[role=option]");
                if (!nodes.length) return;
                active = (i + nodes.length) % nodes.length;
                nodes.forEach(function (n, k) { n.setAttribute("aria-selected", k === active ? "true" : "false"); n.classList.toggle("is-active", k === active); });
                input.setAttribute("aria-activedescendant", nodes[active].id);
                nodes[active].scrollIntoView({ block: "nearest" });
            }
            function render(extra) {
                list.innerHTML = "";
                items.forEach(function (it, i) {
                    var li = doc.createElement("li");
                    li.id = list.id + "-o" + i; li.setAttribute("role", "option"); li.setAttribute("aria-selected", "false");
                    li.innerHTML = opts.render(it);
                    li.addEventListener("mousedown", function (e) { e.preventDefault(); choose(i); });
                    list.appendChild(li);
                });
                if (extra) {
                    var li2 = doc.createElement("li");
                    li2.id = list.id + "-new"; li2.setAttribute("role", "option"); li2.setAttribute("aria-selected", "false"); li2.className = "dcr-opt-create";
                    li2.innerHTML = extra.html;
                    li2.addEventListener("mousedown", function (e) { e.preventDefault(); extra.onChoose(); close(); });
                    list.appendChild(li2);
                }
                if (!items.length && !extra) {
                    var empty = doc.createElement("li");
                    empty.className = "dcr-opt-empty"; empty.textContent = T("Dcc_NoResults");
                    list.appendChild(empty);
                }
                open();
                if (list.querySelector("[role=option]")) setActive(0);
            }
            function choose(i) { var it = items[i]; if (!it) return; close(); opts.onSelect(it); }

            var search = debounce(function () {
                var mine = ++seq, q = input.value;
                opts.fetch(q).then(function (res) {
                    if (mine !== seq) return;
                    items = res || [];
                    render(opts.extra ? opts.extra(q, items) : null);
                }).catch(function () { if (mine === seq) close(); });
            }, DEBOUNCE_MS);

            input.addEventListener("input", function () { if (opts.onType) opts.onType(input.value); search(); });
            input.addEventListener("focus", function () { if (opts.openOnFocus !== false) search(); });
            input.addEventListener("blur", function () { setTimeout(close, 120); });
            input.addEventListener("keydown", function (e) {
                if (e.key === "ArrowDown") { e.preventDefault(); if (list.hidden) search(); else setActive(active + 1); }
                else if (e.key === "ArrowUp") { e.preventDefault(); setActive(active - 1); }
                else if (e.key === "Enter" && !list.hidden && active >= 0) {
                    e.preventDefault();
                    var node = list.querySelectorAll("[role=option]")[active];
                    if (node && node.classList.contains("dcr-opt-create")) { node.dispatchEvent(new MouseEvent("mousedown", { bubbles: true })); }
                    else choose(active);
                }
                else if (e.key === "Escape" && !list.hidden) { e.stopPropagation(); close(); }
            });
            return { close: close };
        }

        // ------------------------------------------------------------ Jalali date field (typed or picked); the hidden truth is the ISO string
        function dateField(wrap, onChange) {
            var input = wrap.querySelector("input"), btn = wrap.querySelector(".dcr-date-btn"), cal = wrap.querySelector(".dcr-cal");
            var isoEl = doc.getElementById(input.id + "Iso") || doc.getElementById(input.getAttribute("aria-describedby") || "");
            var iso = "", view = null;

            function setIso(next, silent) {
                iso = next || "";
                var j = Jalali.isoToJalali(iso);
                input.value = j ? Jalali.formatJalali(j) : (silent === "keep" ? input.value : "");
                if (isoEl) isoEl.textContent = iso;
                if (onChange && silent !== true) onChange(iso);
            }
            function fromText() {
                var text = input.value.trim();
                if (!text) { iso = ""; if (isoEl) isoEl.textContent = ""; input.classList.remove("is-badformat"); if (onChange) onChange(""); return; }
                var j = Jalali.parseJalali(text);
                if (!j) { iso = ""; input.classList.add("is-badformat"); if (isoEl) isoEl.textContent = T("Dcc_DateFormatHint"); if (onChange) onChange(""); return; }
                input.classList.remove("is-badformat");
                iso = Jalali.jalaliToIso(j);
                if (isoEl) isoEl.textContent = iso;
                if (onChange) onChange(iso);
            }
            input.addEventListener("input", fromText);
            input.addEventListener("blur", function () { if (iso) input.value = Jalali.formatJalali(Jalali.isoToJalali(iso)); });

            function drawCal() {
                var first = Jalali.weekdayIndex(view.y, view.m, 1), count = Jalali.daysInMonth(view.y, view.m);
                var sel = Jalali.isoToJalali(iso), today = Jalali.isoToJalali(Jalali.todayIso());
                var html = '<div class="dcr-cal-head"><button type="button" data-nav="-1" aria-label="' + esc(T("Dcc_PrevMonth")) + '"><i class="bi bi-chevron-right"></i></button>'
                    + '<strong>' + MONTHS[view.m - 1] + " " + view.y + '</strong>'
                    + '<button type="button" data-nav="1" aria-label="' + esc(T("Dcc_NextMonth")) + '"><i class="bi bi-chevron-left"></i></button></div><div class="dcr-cal-grid" role="grid">';
                WEEKDAYS.forEach(function (w) { html += '<span class="dcr-cal-wd" aria-hidden="true">' + w + "</span>"; });
                for (var b = 0; b < first; b++) html += "<span></span>";
                for (var d = 1; d <= count; d++) {
                    var cls = "dcr-cal-day" + (sel && sel.y === view.y && sel.m === view.m && sel.d === d ? " is-selected" : "")
                        + (today && today.y === view.y && today.m === view.m && today.d === d ? " is-today" : "");
                    html += '<button type="button" class="' + cls + '" data-day="' + d + '">' + d + "</button>";
                }
                html += '</div><div class="dcr-cal-foot"><button type="button" data-today="1">' + esc(T("Dcc_Today")) + "</button></div>";
                cal.innerHTML = html;
            }
            function openCal() {
                var j = Jalali.isoToJalali(iso) || Jalali.isoToJalali(Jalali.todayIso());
                view = { y: j.y, m: j.m };
                drawCal(); cal.hidden = false; btn.setAttribute("aria-expanded", "true");
                var focusTarget = cal.querySelector(".is-selected") || cal.querySelector(".is-today") || cal.querySelector(".dcr-cal-day");
                if (focusTarget) focusTarget.focus();
            }
            function closeCal(returnFocus) { cal.hidden = true; btn.setAttribute("aria-expanded", "false"); if (returnFocus) btn.focus(); }

            btn.addEventListener("click", function () { cal.hidden ? openCal() : closeCal(true); });
            cal.addEventListener("click", function (e) {
                var t = e.target.closest("button"); if (!t) return;
                if (t.dataset.nav) { view.m += +t.dataset.nav; if (view.m > 12) { view.m = 1; view.y++; } if (view.m < 1) { view.m = 12; view.y--; } drawCal(); return; }
                if (t.dataset.day) { setIso(Jalali.jalaliToIso({ y: view.y, m: view.m, d: +t.dataset.day })); closeCal(true); return; }
                if (t.dataset.today) { setIso(Jalali.todayIso()); closeCal(true); }
            });
            cal.addEventListener("keydown", function (e) {
                if (e.key === "Escape") { e.stopPropagation(); closeCal(true); return; }
                var days = Array.prototype.slice.call(cal.querySelectorAll(".dcr-cal-day")), i = days.indexOf(doc.activeElement);
                if (i < 0) return;
                var move = { ArrowLeft: 1, ArrowRight: -1, ArrowDown: 7, ArrowUp: -7 }[e.key];   // RTL grid: left = next day
                if (move !== undefined && days[i + move]) { e.preventDefault(); days[i + move].focus(); }
            });
            doc.addEventListener("mousedown", function (e) { if (!cal.hidden && !wrap.contains(e.target)) closeCal(false); });

            return { get: function () { return iso; }, set: setIso };
        }

        // ------------------------------------------------------------ field errors
        function clearErrors() {
            form.querySelectorAll(".dcr-field.is-invalid").forEach(function (f) { f.classList.remove("is-invalid"); });
            form.querySelectorAll(".dcr-error").forEach(function (e) { e.textContent = ""; });
            form.querySelectorAll("[aria-invalid]").forEach(function (c) { c.removeAttribute("aria-invalid"); });
            $("dcrSummary").hidden = true; $("dcrDuplicate").hidden = true;
        }
        function showErrors(errors) {
            var list = $("dcrSummaryList"); list.innerHTML = "";
            errors.forEach(function (er) {
                var field = form.querySelector('[data-field="' + er.field + '"]');
                var msg = er.message || T("Dcc_Err_" + er.code);
                if (field) {
                    field.classList.add("is-invalid");
                    var slot = field.querySelector('[data-error-for="' + er.field + '"]');
                    if (slot) slot.textContent = slot.textContent ? slot.textContent + " · " + msg : msg;
                    var ctl = field.querySelector("input:not([type=radio]):not([type=hidden]), select, textarea, .dcr-drop");
                    if (ctl) ctl.setAttribute("aria-invalid", "true");
                }
                var li = doc.createElement("li"), a = doc.createElement("button");
                a.type = "button"; a.className = "dcr-link"; a.textContent = msg;
                a.addEventListener("click", function () {
                    var c = field && field.querySelector("input:not([type=radio]):not([type=hidden]), select, textarea, .dcr-drop");
                    if (c) { c.scrollIntoView({ block: "center" }); c.focus(); }
                });
                li.appendChild(a); list.appendChild(li);
            });
            if (errors.length) {
                var box = $("dcrSummary"); box.hidden = false; box.focus(); box.scrollIntoView({ block: "nearest" });
            }
        }

        // ------------------------------------------------------------ dates
        var docDate = dateField($("dcrDocDateWrap"), markDirty);
        var recvDate = dateField($("dcrRecvDateWrap"), function () { refreshNumber(); refreshDuePresets(); markDirty(); });
        var dueDate = dateField($("dcrDueWrap"), function () { refreshDuePresets(); markDirty(); });
        recvDate.set(Jalali.todayIso(), true);

        function refreshDuePresets() {
            rootEl.querySelectorAll("[data-preset]").forEach(function (b) {
                b.classList.toggle("is-on", !!dueDate.get() && dueDate.get() === presetDue(recvDate.get(), +b.dataset.preset));
            });
        }
        rootEl.querySelectorAll("[data-preset]").forEach(function (b) {
            b.addEventListener("click", function () { dueDate.set(presetDue(recvDate.get(), +b.dataset.preset)); });
        });

        function markDirty() { state.dirty = true; }
        form.addEventListener("input", markDirty);

        // ------------------------------------------------------------ number preview
        var refreshNumber = debounce(function () {
            var pid = $("dcrProject").value;
            getJson(ds.urlNumber, { projectId: pid, receivedDate: recvDate.get() || Jalali.todayIso() })
                .then(function (r) { $("dcrNumber").textContent = r.number; }).catch(function () { /* preview is optional */ });
        }, 150);
        $("dcrProject").addEventListener("change", function () { refreshNumber(); fillContracts(); loadLinksHint(); });

        // ------------------------------------------------------------ party
        var partyInput = $("dcrParty");

        function setParty(p, replyId) {
            state.party = p; state.newPartyName = "";
            $("dcrNewParty").hidden = true;
            partyInput.value = p ? p.name : "";
            fillContracts();
            checkDuplicate();
            if (p) { loadThread(p.id); loadReplyOptions(p.id, replyId); }
            else { $("dcrThread").innerHTML = '<div class="dcr-empty"><i class="bi bi-chat-left-text"></i><span>' + esc(T("Dcc_ThreadPickParty") || "") + "</span></div>"; resetReply(); }
        }

        combobox({
            input: partyInput, list: $("dcrPartyList"),
            fetch: function (q) { return getJson(ds.urlParties, { q: q, projectId: $("dcrProject").value }); },
            render: function (p) {
                return '<span class="dcr-opt-main">' + esc(p.name) + (p.isProvisional ? ' <span class="dcr-badge">' + esc(T("Dcc_Provisional")) + "</span>" : "") + "</span>"
                    + '<span class="dcr-opt-sub">' + esc(p.partyType) + (p.code ? " · " + esc(p.code) : "") + "</span>";
            },
            extra: function (q, found) {
                var name = (q || "").trim();
                if (!name || found.some(function (p) { return p.name.toLowerCase() === name.toLowerCase(); })) return null;
                return {
                    html: '<i class="bi bi-plus-circle"></i> ' + esc(T("Dcc_AddNewParty").replace("{0}", name)),
                    onChoose: function () { state.party = null; state.newPartyName = name; $("dcrNewParty").hidden = false; fillContracts(); resetReply(); }
                };
            },
            onType: function () {
                if (state.party) { state.party = null; fillContracts(); resetReply(); }
                state.newPartyName = "";
                $("dcrNewParty").hidden = true;
            },
            onSelect: setParty
        });

        function fillContracts() {
            var sel = $("dcrContract"), contracts = (state.party && state.party.contracts) || [];
            var keep = sel.value;
            sel.innerHTML = "";
            if (!contracts.length) {
                sel.disabled = true;
                sel.innerHTML = '<option value="">' + esc(state.party ? T("Dcc_NoContracts") : "—") + "</option>";
                return;
            }
            sel.disabled = false;
            sel.innerHTML = '<option value="">' + esc(T("Dcc_ChooseContract")) + "</option>"
                + contracts.map(function (c) { return '<option value="' + c.contractId + '">' + esc(c.contractNo) + "</option>"; }).join("");
            if (contracts.length === 1) sel.value = String(contracts[0].contractId);
            else if (keep) sel.value = keep;
        }

        // refreshing party data when the project changes keeps the contract list project-specific
        $("dcrProject").addEventListener("change", function () {
            if (!state.party) return;
            getJson(ds.urlParties, { q: state.party.name, projectId: $("dcrProject").value }).then(function (list) {
                var p = (list || []).filter(function (x) { return x.id === state.party.id; })[0];
                if (p) { state.party.contracts = p.contracts; fillContracts(); }
            }).catch(function () { });
        });

        // ------------------------------------------------------------ in-reply-to
        function resetReply() {
            var s = $("dcrReply"); s.innerHTML = '<option value="">' + esc(T("Dcc_ReplyChooseParty")) + "</option>"; s.disabled = true;
        }
        function loadReplyOptions(partyId, presetId) {
            getJson(ds.urlOutgoing, { partyId: partyId }).then(function (list) {
                var s = $("dcrReply");
                s.disabled = false;
                s.innerHTML = '<option value="">' + esc(list.length ? T("Dcc_ReplyChoose") : T("Dcc_ReplyNone")) + "</option>"
                    + list.map(function (l) {
                        return '<option value="' + l.letterId + '">' + (l.awaitingReply ? "● " : "") + esc(l.letterNumber) + " — " + esc(l.subject) + "</option>";
                    }).join("");
                if (presetId) s.value = String(presetId);
            }).catch(function () { });
        }

        // ------------------------------------------------------------ party thread (side panel)
        function renderThread(t) {
            var el = $("dcrThread");
            var count = $("dcrThreadCount");
            count.textContent = t.total; count.hidden = !t.total;
            var kpis = '<div class="dcr-kpis">'
                + '<div class="dcr-kpi' + (t.awaitingUs ? " is-hot" : "") + '"><strong>' + t.awaitingUs + "</strong><span>" + esc(T("Dcc_ThreadAwaitingUs")) + "</span></div>"
                + '<div class="dcr-kpi"><strong>' + t.awaitingThem + "</strong><span>" + esc(T("Dcc_ThreadAwaitingThem")) + "</span></div>"
                + '<div class="dcr-kpi"><strong class="dcr-ltr">' + (t.lastContact ? Jalali.formatJalali(Jalali.isoToJalali(t.lastContact)) : "—") + "</strong><span>" + esc(T("Dcc_ThreadLast")) + "</span></div></div>";
            if (!t.items.length) { el.innerHTML = kpis + '<div class="dcr-empty"><i class="bi bi-chat-left-text"></i><span>' + esc(T("Dcc_ThreadEmpty")) + "</span></div>"; return; }
            el.innerHTML = kpis + '<ol class="dcr-thread">' + t.items.map(function (i) {
                var incoming = i.direction === "Incoming";
                var j = Jalali.isoToJalali(i.date);
                var url = ds.urlLetter.replace("__ID__", i.letterId);
                var flags = (i.overdue ? '<span class="dcr-flag dcr-flag--bad">' + esc(T("Dcc_Overdue")) + "</span>" : "")
                    + (i.responseRequired && i.answered ? '<span class="dcr-flag dcr-flag--ok">' + esc(T("Dcc_Answered")) + "</span>" : "")
                    + (i.responseRequired && !i.answered && !i.overdue && i.responseDueDate ? '<span class="dcr-flag">' + esc(T("Dcc_DueOn")) + " " + Jalali.formatJalali(Jalali.isoToJalali(i.responseDueDate)) + "</span>" : "");
                return '<li class="dcr-thread-item ' + (incoming ? "is-in" : "is-out") + '">'
                    + '<i class="bi ' + (incoming ? "bi-box-arrow-in-down" : "bi-box-arrow-up") + '" aria-hidden="true" title="' + esc(incoming ? T("Dcc_Incoming") : T("Dcc_Outgoing")) + '"></i>'
                    + '<div><a class="dcr-link dcr-ltr" href="' + url + '" target="_blank" rel="noopener">' + esc(i.letterNumber) + "</a> "
                    + '<span class="dcr-sub-date dcr-ltr">' + (j ? Jalali.formatJalali(j) : "") + "</span> " + flags
                    + '<div class="dcr-thread-subject">' + esc(i.subject) + "</div></div></li>";
            }).join("") + "</ol>";
        }
        function loadThread(partyId) {
            $("dcrThread").innerHTML = '<div class="dcr-empty"><span>' + esc(T("Dcc_Searching")) + "</span></div>";
            getJson(ds.urlThread, { partyId: partyId }).then(function (t) { if (state.party && state.party.id === partyId) renderThread(t); })
                .catch(function () { $("dcrThread").innerHTML = '<div class="dcr-empty"><span>' + esc(T("Dcc_NetworkError")) + "</span></div>"; });
        }

        // ------------------------------------------------------------ duplicate hint (live) and sender-reference requirement
        var checkDuplicate = debounce(function () {
            var ref = $("dcrRef").value.trim();
            var hint = $("dcrRefHint"); hint.textContent = ""; hint.classList.remove("is-warn");
            if (!state.party || !ref) return;
            getJson(ds.urlDuplicate, { partyId: state.party.id, reference: ref }).then(function (d) {
                if (!d) return;
                hint.classList.add("is-warn");
                hint.textContent = T("Dcc_DuplicateFound").replace("{0}", d.letterNumber);
            }).catch(function () { });
        }, 350);
        $("dcrRef").addEventListener("input", function () { state.confirmDuplicate = false; checkDuplicate(); });

        function channelValue() { var r = form.querySelector('input[name="receiptChannel"]:checked'); return r ? r.value : "OfficialLetter"; }
        function refreshRefRequired() { $("dcrRefReq").hidden = channelValue() !== "OfficialLetter"; }
        form.querySelectorAll('input[name="receiptChannel"]').forEach(function (r) { r.addEventListener("change", refreshRefRequired); });
        refreshRefRequired();

        // ------------------------------------------------------------ action: response required / owner / cc
        function refreshResponse() {
            var on = $("dcrRespReq").checked;
            $("dcrDueBlock").hidden = !on;
            $("dcrOwnerReq").hidden = !on;
            if (on && !dueDate.get()) dueDate.set(presetDue(recvDate.get(), 7));
        }
        $("dcrRespReq").addEventListener("change", refreshResponse);

        function personRender(p) { return '<span class="dcr-opt-main">' + esc(p.name) + '</span><span class="dcr-opt-sub">' + esc([p.position, p.department].filter(Boolean).join(" · ")) + "</span>"; }
        combobox({
            input: $("dcrOwner"), list: $("dcrOwnerList"), render: personRender,
            fetch: function (q) { return getJson(ds.urlPeople, { q: q }); },
            onType: function () { state.owner = null; },
            onSelect: function (p) { state.owner = { employeeId: p.employeeId, name: p.name }; $("dcrOwner").value = p.name; }
        });
        combobox({
            input: $("dcrCc"), list: $("dcrCcList"), render: personRender,
            fetch: function (q) { return getJson(ds.urlPeople, { q: q }); },
            onSelect: function (p) {
                if (!state.cc.some(function (c) { return c.employeeId === p.employeeId; })) state.cc.push({ employeeId: p.employeeId, name: p.name });
                $("dcrCc").value = ""; renderCc();
            }
        });
        function renderCc() {
            var el = $("dcrCcTags"); el.innerHTML = "";
            state.cc.forEach(function (c, i) {
                var tag = doc.createElement("span"); tag.className = "dcr-tag";
                tag.innerHTML = esc(c.name) + ' <button type="button" aria-label="' + esc(T("Dcc_Remove")) + " " + esc(c.name) + '"><i class="bi bi-x"></i></button>';
                tag.querySelector("button").addEventListener("click", function () { state.cc.splice(i, 1); renderCc(); });
                el.appendChild(tag);
            });
        }

        // ------------------------------------------------------------ files + preview
        var drop = $("dcrDrop"), fileInput = $("dcrFileInput"), previewUrl = null;

        function addFiles(fileList) {
            Array.prototype.forEach.call(fileList, function (f) {
                var hasOriginal = state.files.some(function (x) { return x.role === "Original"; });
                state.files.push({ uid: ++state.uid, file: f, role: hasOriginal ? "Attachment" : "Original" });
            });
            renderFiles();
            var shown = state.files.filter(function (x) { return x.role === "Original"; })[0] || state.files[0];
            if (shown) preview(shown);
            markDirty();
        }
        drop.addEventListener("click", function () { fileInput.click(); });
        drop.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fileInput.click(); } });
        fileInput.addEventListener("change", function () { addFiles(fileInput.files); fileInput.value = ""; });
        ["dragenter", "dragover"].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); drop.classList.add("is-over"); }); });
        ["dragleave", "drop"].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); drop.classList.remove("is-over"); }); });
        drop.addEventListener("drop", function (e) { if (e.dataTransfer && e.dataTransfer.files.length) addFiles(e.dataTransfer.files); });

        function renderFiles() {
            var ul = $("dcrFiles"); ul.innerHTML = "";
            var pv = $("dcrPreviewList"); pv.innerHTML = "";
            state.files.forEach(function (f) {
                var li = doc.createElement("li"); li.className = "dcr-file";
                li.innerHTML = '<i class="bi ' + (fileKind(f.file.name) === "pdf" ? "bi-file-earmark-pdf" : fileKind(f.file.name) === "image" ? "bi-file-earmark-image" : "bi-file-earmark") + '" aria-hidden="true"></i>'
                    + '<div class="dcr-file-name"><span>' + esc(f.file.name) + '</span><small class="dcr-ltr">' + formatBytes(f.file.size) + "</small></div>"
                    + '<select class="form-select form-select-sm" aria-label="' + esc(f.file.name) + '">'
                    + ["Original", "Attachment", "EmailCopy"].map(function (r) { return '<option value="' + r + '"' + (f.role === r ? " selected" : "") + ">" + esc(T("Dcc_Role" + r)) + "</option>"; }).join("")
                    + '</select><button type="button" class="dcr-icon" aria-label="' + esc(T("Dcc_RemoveFile")) + " " + esc(f.file.name) + '"><i class="bi bi-trash3"></i></button>';
                li.querySelector("select").addEventListener("change", function (e) { f.role = e.target.value; });
                li.querySelector("button").addEventListener("click", function () { state.files = state.files.filter(function (x) { return x !== f; }); renderFiles(); if (!state.files.length) resetPreview(); });
                ul.appendChild(li);

                var pli = doc.createElement("li");
                var pb = doc.createElement("button"); pb.type = "button"; pb.textContent = f.file.name;
                pb.addEventListener("click", function () { preview(f); });
                pli.appendChild(pb); pv.appendChild(pli);
            });
        }
        function resetPreview() {
            if (previewUrl) { URL.revokeObjectURL(previewUrl); previewUrl = null; }
            $("dcrPreview").innerHTML = '<div class="dcr-empty"><i class="bi bi-file-earmark-text"></i><span>' + esc(doc.querySelector("#dcrPreview").dataset.empty || "") + "</span></div>";
        }
        function preview(f) {
            if (!f) return;
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = URL.createObjectURL(f.file);
            var kind = fileKind(f.file.name), box = $("dcrPreview");
            if (kind === "pdf") box.innerHTML = '<iframe title="' + esc(f.file.name) + '" src="' + previewUrl + '"></iframe>';
            else if (kind === "image") box.innerHTML = '<img alt="' + esc(f.file.name) + '" src="' + previewUrl + '" />';
            else box.innerHTML = '<div class="dcr-empty"><i class="bi bi-file-earmark"></i><span>' + esc(T("Dcc_NoFilePreview")) + "</span><strong>" + esc(f.file.name) + "</strong></div>";
            showTab("docs");
        }

        // ------------------------------------------------------------ links
        function loadLinksHint() { /* the search below reads the current project each time */ }
        combobox({
            input: $("dcrLinkSearch"), list: $("dcrLinkList"),
            fetch: function (q) { return getJson(ds.urlLinks, { type: $("dcrLinkType").value, q: q, projectId: $("dcrProject").value }); },
            render: function (l) { return '<span class="dcr-opt-main dcr-ltr">' + esc(l.number) + '</span><span class="dcr-opt-sub">' + esc(l.title || "") + "</span>"; },
            onSelect: function (l) {
                if (!state.links.some(function (x) { return x.linkType === l.linkType && x.targetId === l.targetId; })) state.links.push(l);
                $("dcrLinkSearch").value = ""; renderLinks(); markDirty();
            }
        });
        $("dcrLinkType").addEventListener("change", function () { $("dcrLinkSearch").value = ""; });
        function renderLinks() {
            var ul = $("dcrLinks"); ul.innerHTML = "";
            state.links.forEach(function (l, i) {
                var li = doc.createElement("li"); li.className = "dcr-linkitem";
                li.innerHTML = '<span class="dcr-badge">' + esc(T("Dcc_Link" + l.linkType)) + '</span><bdi class="dcr-ltr">' + esc(l.number) + "</bdi><span>" + esc(l.title || "") + "</span>"
                    + '<button type="button" class="dcr-icon" aria-label="' + esc(T("Dcc_Remove")) + " " + esc(l.number) + '"><i class="bi bi-x-lg"></i></button>';
                li.querySelector("button").addEventListener("click", function () { state.links.splice(i, 1); renderLinks(); });
                ul.appendChild(li);
            });
        }

        // ------------------------------------------------------------ side tabs
        function showTab(which) {
            var docs = which === "docs";
            $("dcrTabDocs").classList.toggle("active", docs); $("dcrTabDocs").setAttribute("aria-selected", String(docs)); $("dcrTabDocs").tabIndex = docs ? 0 : -1;
            $("dcrTabThread").classList.toggle("active", !docs); $("dcrTabThread").setAttribute("aria-selected", String(!docs)); $("dcrTabThread").tabIndex = docs ? -1 : 0;
            $("dcrPaneDocs").hidden = !docs; $("dcrPaneThread").hidden = docs;
        }
        $("dcrTabDocs").addEventListener("click", function () { showTab("docs"); });
        $("dcrTabThread").addEventListener("click", function () { showTab("thread"); });
        rootEl.querySelector(".dcr-tabs").addEventListener("keydown", function (e) {
            if (e.key === "ArrowLeft" || e.key === "ArrowRight") { e.preventDefault(); var toThread = $("dcrTabDocs").classList.contains("active"); showTab(toThread ? "thread" : "docs"); (toThread ? $("dcrTabThread") : $("dcrTabDocs")).focus(); }
        });

        // ------------------------------------------------------------ submit
        function clientChecks() {
            var e = [];
            if (!$("dcrProject").value) e.push({ field: "projectId", code: "ProjectRequired" });
            if (!state.party && !state.newPartyName) e.push({ field: "party", code: "PartyRequired" });
            if (!$("dcrSubject").value.trim()) e.push({ field: "subject", code: "SubjectRequired" });
            if (channelValue() === "OfficialLetter" && !$("dcrRef").value.trim()) e.push({ field: "senderReference", code: "SenderReferenceRequired" });
            if (!docDate.get()) e.push({ field: "documentDate", code: $("dcrDocDate").value.trim() ? "DateInvalid" : "DocumentDateRequired" });
            if (!recvDate.get()) e.push({ field: "receivedDate", code: "DateInvalid" });
            if ($("dcrRespReq").checked) {
                if (!dueDate.get()) e.push({ field: "responseDueDate", code: "ResponseDueRequired" });
                if (!state.owner) e.push({ field: "ownerEmployeeId", code: "OwnerRequired" });
            }
            var originals = state.files.filter(function (f) { return f.role === "Original"; }).length;
            if (originals === 0) e.push({ field: "files", code: "OriginalRequired" });
            else if (originals > 1) e.push({ field: "files", code: "OneOriginalOnly" });
            return e;
        }

        function buildFormData() {
            var fd = new FormData();
            fd.append("__RequestVerificationToken", form.querySelector('input[name="__RequestVerificationToken"]').value);
            function put(k, v) { if (v !== undefined && v !== null && v !== "") fd.append(k, v); }
            put("projectId", $("dcrProject").value);
            put("contractId", $("dcrContract").disabled ? "" : $("dcrContract").value);
            if (state.party) put("correspondentPartyId", state.party.id);
            else { put("newPartyName", state.newPartyName); put("newPartyType", $("dcrNewPartyType").value); }
            put("senderReference", $("dcrRef").value.trim());
            put("documentDate", docDate.get());
            put("receivedDate", recvDate.get());
            put("receiptChannel", channelValue());
            put("subject", $("dcrSubject").value.trim());
            put("summary", $("dcrSummaryText").value.trim());
            put("priority", $("dcrPriority").value);
            put("category", $("dcrCategory").value);
            put("confidentialityLevel", $("dcrConf").value);
            put("disciplineId", $("dcrDiscipline").value);
            put("parentLetterId", $("dcrReply").disabled ? "" : $("dcrReply").value);
            fd.append("responseRequired", $("dcrRespReq").checked ? "true" : "false");
            if ($("dcrRespReq").checked) put("responseDueDate", dueDate.get());
            if (state.owner) put("ownerEmployeeId", state.owner.employeeId);
            state.cc.forEach(function (c) { fd.append("ccEmployeeIds", c.employeeId); });
            put("instruction", $("dcrInstruction").value.trim());
            fd.append("links", JSON.stringify(state.links.map(function (l) { return { linkType: l.linkType, targetId: l.targetId }; })));
            fd.append("confirmDuplicate", state.confirmDuplicate ? "true" : "false");
            state.files.forEach(function (f) { fd.append("files", f.file, f.file.name); fd.append("fileRoles", f.role); });
            return fd;
        }

        var labels = {};
        function setBusy(on, mode) {
            state.submitting = on;
            var btn = $(mode === "next" ? "dcrSubmitNext" : "dcrSubmit");
            [$("dcrSubmit"), $("dcrSubmitNext")].forEach(function (b) { b.disabled = on; });
            if (on) {
                labels[btn.id] = btn.innerHTML;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> ' + esc(T("Dcc_Registering"));
            } else if (labels[btn.id]) {
                btn.innerHTML = labels[btn.id];
            }
        }

        function submit(mode) {
            if (state.submitting) return;
            clearErrors();
            var local = clientChecks();
            if (local.length) { showErrors(local); return; }

            setBusy(true, mode);
            fetch(ds.urlRegister, { method: "POST", body: buildFormData(), credentials: "same-origin", headers: { "Accept": "application/json" } })
                .then(function (r) { return r.json().then(function (j) { return { ok: r.ok, json: j }; }); })
                .then(function (res) {
                    setBusy(false, mode);
                    if (res.ok && res.json.success) {
                        state.dirty = false;
                        if (mode === "next") finishNext(res.json); else window.location.href = res.json.detailsUrl;
                        return;
                    }
                    var dup = res.json.duplicate;
                    if (dup) {
                        $("dcrDuplicateText").textContent = T("Dcc_DuplicateFound").replace("{0}", dup.letterNumber);
                        $("dcrDuplicateLink").href = dup.url;
                        $("dcrDuplicate").hidden = false;
                        $("dcrDuplicate").scrollIntoView({ block: "nearest" });
                        pendingMode = mode;
                        return;
                    }
                    showErrors(res.json.errors || []);
                })
                .catch(function () { setBusy(false, mode); showErrors([{ field: "", code: "NetworkError", message: T("Dcc_NetworkError") }]); });
        }
        var pendingMode = "open";
        $("dcrDuplicateConfirm").addEventListener("click", function () { state.confirmDuplicate = true; $("dcrDuplicate").hidden = true; submit(pendingMode); });

        // Enter inside a single-line field must never register the letter by accident (use the buttons or Ctrl+Enter)
        form.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && e.target.tagName === "INPUT" && e.target.type !== "checkbox" && e.target.type !== "radio" && !e.defaultPrevented) e.preventDefault();
        });

        form.addEventListener("submit", function (e) {
            e.preventDefault();
            submit(e.submitter && e.submitter.dataset.mode === "next" ? "next" : "open");
        });
        doc.addEventListener("keydown", function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key === "Enter") { e.preventDefault(); submit(e.shiftKey ? "next" : "open"); }
        });
        window.addEventListener("beforeunload", function (e) { if (state.dirty && !state.submitting) { e.preventDefault(); e.returnValue = T("Dcc_Unsaved"); } });

        function finishNext(result) {
            var keep = $("dcrKeepSender").checked;
            var keepParty = keep ? state.party : null, keepProject = $("dcrProject").value, keepContract = $("dcrContract").value, keepChannel = channelValue();
            var newPartyName = keep && !state.party ? state.newPartyName : "";

            form.reset();
            state.files = []; state.links = []; state.cc = []; state.owner = null; state.confirmDuplicate = false;
            renderFiles(); renderLinks(); renderCc(); resetPreview();
            docDate.set("", true); dueDate.set("", true); recvDate.set(Jalali.todayIso(), true);
            $("dcrDocDate").value = ""; $("dcrDue").value = "";
            refreshResponse(); refreshRefRequired(); refreshDuePresets(); $("dcrRefHint").textContent = "";
            $("dcrNewParty").hidden = true; resetReply(); partyInput.value = "";
            state.party = null; state.newPartyName = "";

            if (keep) {
                $("dcrProject").value = keepProject;
                var radio = form.querySelector('input[name="receiptChannel"][value="' + keepChannel + '"]'); if (radio) radio.checked = true;
                if (keepParty) { setParty(keepParty); if (keepContract) $("dcrContract").value = keepContract; }
                else if (newPartyName) { partyInput.value = newPartyName; state.newPartyName = newPartyName; $("dcrNewParty").hidden = false; }
            } else {
                $("dcrThread").innerHTML = "";
            }
            refreshNumber();

            $("dcrSavedText").textContent = T("Dcc_SavedNext").replace("{0}", result.letterNumber);
            $("dcrSavedLink").href = result.detailsUrl;
            $("dcrSaved").hidden = false;
            window.scrollTo({ top: 0, behavior: "smooth" });
            (keep && keepParty ? $("dcrRef") : partyInput).focus();
        }

        // ------------------------------------------------------------ preset party / reply-to (coming from an outgoing letter or a party link)
        if (ds.presetParty) {
            try {
                var replyTarget = ds.replyTo ? JSON.parse(ds.replyTo) : null;
                setParty(JSON.parse(ds.presetParty), replyTarget && replyTarget.id);
            } catch (e) { /* ignore a malformed preset; the form still works empty */ }
        }
        refreshNumber();

        return { state: state, submit: submit, docDate: docDate, recvDate: recvDate, dueDate: dueDate };
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", function () { init(document); });
        else init(document);
    }

    return { init: init, presetDue: presetDue, formatBytes: formatBytes, fileKind: fileKind, debounce: debounce };
});
