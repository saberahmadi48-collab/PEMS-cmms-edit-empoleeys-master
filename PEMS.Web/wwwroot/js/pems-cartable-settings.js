// PEMS Correspondence cartable settings modal: general + columns (saved to the server), tag tree, delegation (people picked from the org chart),
// personal routing rules, contact groups and reminders. Every list/save goes through LetterCartableController, which returns localized
// error messages; this file only renders and collects input (DOM nodes are built with createElement and textContent, never from HTML strings).
(function () {
    "use strict";
    var C = window.PemsCartable;
    var modalEl = document.getElementById("ctSettings");
    if (!C || !modalEl) return;

    var el = C.el, t = function (k) { return C.t(k); };
    var loaded = {};
    var msgEl = document.getElementById("ctSettingsMsg");

    function say(text, kind) { msgEl.textContent = text || ""; msgEl.className = "ct-msg me-auto " + (kind || ""); }
    function body(name) { return modalEl.querySelector("[data-pane-body='" + name + "']"); }
    function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }
    function note(text) { return el("div", { class: "ct-empty-note", text: text }); }
    function run(promise, done) {
        promise.then(function (r) {
            if (r && r.ok === false) say(r.error || t("failed"), "err");
            else { say(t("saved"), "ok"); if (done) done(r); }
        }).catch(function () { say(t("failed"), "err"); });
    }
    function field(label, control, cls) {
        var id = control.id || ("f" + Math.random().toString(36).slice(2, 8));
        control.id = id;
        return el("div", { class: cls || "" }, [el("label", { class: "form-label small", for: id, text: label }), control]);
    }
    function input(type, value, attrs) { return el("input", Object.assign({ type: type, class: "form-control form-control-sm", value: value == null ? "" : value }, attrs || {})); }
    function select(options, value, attrs) {
        var s = el("select", Object.assign({ class: "form-select form-select-sm" }, attrs || {}));
        options.forEach(function (o) { var op = el("option", { value: String(o.value), text: o.text }); if (String(o.value) === String(value)) op.selected = true; s.appendChild(op); });
        return s;
    }
    function button(text, cls, onclick, extra) { return el("button", Object.assign({ type: "button", class: "btn btn-sm " + cls, text: text, onclick: onclick }, extra || {})); }
    function isoDay(d) { var p = function (n) { return (n < 10 ? "0" : "") + n; }; return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()); }
    function dayText(s) { return String(s || "").slice(0, 10); }

    // ------------------------------------------------------------------ tabs
    function show(name) {
        Array.prototype.forEach.call(modalEl.querySelectorAll(".ct-settings-nav button"), function (b) { b.setAttribute("aria-selected", b.getAttribute("data-pane") === name ? "true" : "false"); });
        Array.prototype.forEach.call(modalEl.querySelectorAll("[data-pane-body]"), function (p) { p.hidden = p.getAttribute("data-pane-body") !== name; });
        say("");
        // only the display preferences use the footer button; every other section saves as you act
        document.getElementById("ctSettingsSave").hidden = !(name === "general" || name === "columns");
        if (!loaded[name] && renderers[name]) { loaded[name] = true; renderers[name](); }
    }
    modalEl.querySelector(".ct-settings-nav").addEventListener("click", function (e) {
        var b = e.target.closest("button[data-pane]");
        if (b) show(b.getAttribute("data-pane"));
    });
    document.addEventListener("click", function (e) {
        var b = e.target.closest ? e.target.closest("[data-settings-pane]") : null;
        if (!b || !window.bootstrap) return;
        e.preventDefault();
        show(b.getAttribute("data-settings-pane"));
        window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
    });

    // ------------------------------------------------------------------ general + columns
    function fillGeneral() {
        var s = C.cfg.settings || {};
        document.getElementById("ctSetView").value = s.View || "flat";
        document.getElementById("ctSetDensity").value = s.Density || "normal";
        document.getElementById("ctSetPageSize").value = String(s.PageSize || 50);
        document.getElementById("ctSetPreview").checked = !!s.OpenPreviewPane;
        document.getElementById("ctSetConfirmTrash").checked = !!s.ConfirmTrash;
        document.getElementById("ctSetMarkRead").checked = !!s.MarkReadOnOpen;
        document.getElementById("ctSetFrozen").value = String(s.FrozenColumns || 0);
        var hidden = s.HiddenColumns || [];
        Array.prototype.forEach.call(modalEl.querySelectorAll("[data-set-col]"), function (c) { c.checked = hidden.indexOf(c.getAttribute("data-set-col")) === -1; });
    }
    fillGeneral();

    document.getElementById("ctSettingsSave").addEventListener("click", function () {
        var hidden = [];
        Array.prototype.forEach.call(modalEl.querySelectorAll("[data-set-col]"), function (c) { if (!c.checked) hidden.push(c.getAttribute("data-set-col")); });
        var payload = {
            view: document.getElementById("ctSetView").value, density: document.getElementById("ctSetDensity").value,
            hiddenColumns: hidden, frozenColumns: parseInt(document.getElementById("ctSetFrozen").value, 10) || 0,
            openPreviewPane: document.getElementById("ctSetPreview").checked, confirmTrash: document.getElementById("ctSetConfirmTrash").checked,
            markReadOnOpen: document.getElementById("ctSetMarkRead").checked, pageSize: parseInt(document.getElementById("ctSetPageSize").value, 10) || 50
        };
        say(t("loading"));
        C.post(C.cfg.urls.settingsSave, payload).then(function () {
            C.clearLocalViewState();   // the saved server settings become the defaults again
            window.location.href = window.location.pathname + (window.location.search ? window.location.search.replace(/([?&])view=[^&]*&?/, "$1").replace(/[?&]$/, "") : "");
        }).catch(function () { say(t("failed"), "err"); });
    });

    // ------------------------------------------------------------------ tags
    var COLORS = ["primary", "secondary", "success", "danger", "warning", "info", "dark"];
    function renderTags() {
        var pane = body("tags"); clear(pane);
        pane.appendChild(el("h3", { text: modalEl.querySelector("[data-pane='tags']").textContent.trim() }));
        var list = el("ul", { class: "ct-list ct-tree-edit" });
        var formHost = el("div", { class: "mt-3" });
        pane.appendChild(list); pane.appendChild(formHost);

        function load() {
            C.get(C.cfg.urls.tags).then(function (tags) {
                clear(list);
                if (!tags.length) list.appendChild(el("li", {}, [note(t("noItems"))]));
                tags.forEach(function (tag) {
                    var li = el("li", { style: "--depth:" + tag.depth }, [
                        el("span", { class: "ct-tag-dot c-" + tag.color, style: "display:inline-block;width:10px;height:10px;border-radius:50%;background:var(--bs-" + tag.color + ", #888)" }),
                        el("span", { class: "ct-grow", text: tag.name }),
                        el("span", { class: "ct-badge-rel", text: tag.isPersonal ? t("personal") : t("organizational") }),
                        el("span", { class: "ct-sub", text: tag.letterCount ? String(tag.letterCount) : "" })
                    ]);
                    if (tag.canEdit) {
                        li.appendChild(button(t("edit"), "btn-outline-secondary", function () { edit(tag, tags); }));
                        li.appendChild(button(t("delete"), "btn-outline-danger", function () {
                            if (!window.confirm(t("confirmDelete"))) return;
                            run(C.post(C.cfg.urls.tagDelete, { id: tag.tagId }), load);
                        }));
                    }
                    list.appendChild(li);
                });
                edit(null, tags);
            }).catch(function () { clear(list); list.appendChild(el("li", {}, [note(t("failed"))])); });
        }

        function edit(tag, tags) {
            clear(formHost);
            var name = input("text", tag ? tag.name : "", { maxlength: "100" });
            var parentOptions = [{ value: "", text: t("rootTag") }].concat(tags.filter(function (x) { return !tag || x.tagId !== tag.tagId; })
                .map(function (x) { return { value: x.tagId, text: new Array(x.depth).join("— ") + x.name }; }));
            var parent = select(parentOptions, tag && tag.parentTagId ? tag.parentTagId : "");
            var scope = select([{ value: "personal", text: t("scopePersonal") }].concat(C.cfg.canManageOrg ? [{ value: "org", text: t("scopeOrg") }] : []), tag ? (tag.isPersonal ? "personal" : "org") : "personal");
            if (tag) scope.disabled = true;
            var order = input("number", tag ? tag.sortOrder : 0, { min: "0", max: "9999" });
            var chosen = tag ? tag.color : "secondary";
            var colors = el("div", { class: "ct-colors", role: "radiogroup", "aria-label": t("colour") });
            COLORS.forEach(function (c) {
                var id = "ctColor_" + c;
                var r = el("input", { type: "radio", name: "ctTagColor", id: id, value: c }); if (c === chosen) r.checked = true;
                r.addEventListener("change", function () { chosen = c; });
                colors.appendChild(r);
                colors.appendChild(el("label", { for: id, title: c, style: "background:var(--bs-" + c + ", #888)" }));
            });
            formHost.appendChild(el("div", { class: "ct-form-row" }, [
                field(t("tagName"), name, "flex-grow-1"), field(t("parentTag"), parent), field("", scope), field(t("order"), order, ""), el("div", {}, [el("div", { class: "form-label small", text: t("colour") }), colors])
            ]));
            formHost.appendChild(button(tag ? t("save") : t("addTag"), "btn-pems-solid", function () {
                run(C.post(C.cfg.urls.tagSave, {
                    tagId: tag ? tag.tagId : null, name: name.value, color: chosen, parentTagId: parent.value ? parseInt(parent.value, 10) : null,
                    personal: scope.value === "personal", sortOrder: parseInt(order.value, 10) || 0
                }), load);
            }));
            if (tag) formHost.appendChild(button(t("cancel"), "btn-link", function () { edit(null, tags); }));
        }
        load();
    }

    // ------------------------------------------------------------------ delegation
    function renderDelegation() {
        var pane = body("delegation"); clear(pane);
        var host = el("div"); pane.appendChild(host);
        function relText(r) { return { Manager: t("relManager"), Subordinate: t("relSubordinate"), Peer: t("relPeer"), Other: t("relOther") }[r] || r; }
        function load() {
            C.get(C.cfg.urls.delegations).then(function (d) {
                clear(host);
                host.appendChild(el("h3", { text: t("givenDelegations") }));
                var given = el("ul", { class: "ct-list" });
                if (!d.given.length) given.appendChild(el("li", {}, [note(t("noItems"))]));
                d.given.forEach(function (x) {
                    var li = el("li", {}, [el("div", { class: "ct-grow" }, [el("div", { text: x.delegateName + (x.delegatePosition ? " — " + x.delegatePosition : "") }),
                        el("div", { class: "ct-sub", text: dayText(x.fromDate) + " → " + dayText(x.toDate) + (x.note ? " · " + x.note : "") })]),
                        el("span", { class: "ct-badge-rel", text: x.isActive ? (x.isEffectiveNow ? t("activeNow") : t("enabled")) : t("revoked") })]);
                    if (x.isActive) li.appendChild(button(t("revoke"), "btn-outline-danger", function () { run(C.post(C.cfg.urls.delegationRevoke, { id: x.letterDelegationId }), load); }));
                    given.appendChild(li);
                });
                host.appendChild(given);

                var cand = el("select", { class: "form-select form-select-sm" });
                [["Manager", d.candidates], ["Peer", d.candidates], ["Subordinate", d.candidates], ["Other", d.candidates]].forEach(function (g) {
                    var items = d.candidates.filter(function (c) { return c.relation === g[0]; });
                    if (!items.length) return;
                    var og = el("optgroup", { label: relText(g[0]) });
                    items.forEach(function (c) { og.appendChild(el("option", { value: String(c.userId), text: c.name + (c.position ? " — " + c.position : "") })); });
                    cand.appendChild(og);
                });
                var from = input("date", isoDay(new Date())); var to = input("date", isoDay(new Date(Date.now() + 7 * 864e5)));
                var nt = input("text", "", { maxlength: "300" });
                host.appendChild(el("div", { class: "mt-3" }, [
                    el("div", { class: "ct-form-row" }, [field(t("delegateTo"), cand, "flex-grow-1"), field(t("fromDate"), from), field(t("toDate"), to), field(t("note"), nt, "flex-grow-1")]),
                    button(t("createDelegation"), "btn-pems-solid", function () {
                        if (!cand.value) return;
                        run(C.post(C.cfg.urls.delegationCreate, { delegateUserId: parseInt(cand.value, 10), fromDate: from.value, toDate: to.value, note: nt.value }), load);
                    })
                ]));

                host.appendChild(el("h3", { class: "mt-4", text: t("receivedDelegations") }));
                var rec = el("ul", { class: "ct-list" });
                if (!d.received.length) rec.appendChild(el("li", {}, [note(t("noItems"))]));
                d.received.forEach(function (x) {
                    rec.appendChild(el("li", {}, [el("div", { class: "ct-grow" }, [el("div", { text: x.delegatorName }),
                        el("div", { class: "ct-sub", text: dayText(x.fromDate) + " → " + dayText(x.toDate) })]),
                        el("span", { class: "ct-badge-rel", text: x.isActive ? (x.isEffectiveNow ? t("activeNow") : t("enabled")) : t("revoked") })]));
                });
                host.appendChild(rec);
            }).catch(function () { clear(host); host.appendChild(note(t("failed"))); });
        }
        load();
    }

    // ------------------------------------------------------------------ routing rules
    function renderRules() {
        var pane = body("rules"); clear(pane);
        var host = el("div"); pane.appendChild(host);
        C.get(C.cfg.urls.rules).then(function (data) {
            var lookups = data.lookups || { projects: [], disciplines: [] };
            var tags = C.cfg.tags || [];
            function list() {
                clear(host);
                var ul = el("ul", { class: "ct-list" });
                if (!data.rules.length) ul.appendChild(el("li", {}, [note(t("noItems"))]));
                data.rules.forEach(function (r) {
                    var cond = [r.matchLetterType, r.matchPriority, r.matchCategory, r.matchSenderContains && ("«" + r.matchSenderContains + "»"), r.matchSubjectContains && ("«" + r.matchSubjectContains + "»")].filter(Boolean).join(" · ");
                    var act = [r.applyTagName && ("# " + r.applyTagName), r.starLetter && "★", r.referToName && ("→ " + r.referToName)].filter(Boolean).join(" · ");
                    var chk = el("input", { type: "checkbox", class: "form-check-input", "aria-label": t("enabled") }); chk.checked = r.isActive;
                    chk.addEventListener("change", function () { run(C.post(C.cfg.urls.ruleToggle, { id: r.letterRoutingRuleId, active: chk.checked }), reload); });
                    ul.appendChild(el("li", {}, [chk, el("div", { class: "ct-grow" }, [el("div", { class: "fw-semibold", text: r.name }),
                        el("div", { class: "ct-sub", text: t("ifLabel") + ": " + (cond || "—") }), el("div", { class: "ct-sub", text: t("thenLabel") + ": " + (act || "—") })]),
                        el("span", { class: "ct-sub", text: t("matches") + ": " + r.matchCount }),
                        button(t("edit"), "btn-outline-secondary", function () { form(r); }),
                        button(t("delete"), "btn-outline-danger", function () { if (window.confirm(t("confirmDelete"))) run(C.post(C.cfg.urls.ruleDelete, { id: r.letterRoutingRuleId }), reload); })]));
                });
                host.appendChild(ul);
                host.appendChild(el("div", { class: "mt-3" }, [button(t("addRule"), "btn-pems-solid", function () { form(null); })]));
            }
            function reload() { loaded.rules = false; renderRules(); }
            function opts(items, anyText) { return [{ value: "", text: anyText }].concat(items.map(function (x) { return { value: x.id, text: x.name }; })); }
            function form(r) {
                clear(host);
                r = r || {};
                var name = input("text", r.name || "", { maxlength: "120" });
                var type = select([{ value: "", text: t("anyType") }, { value: "Incoming", text: "Incoming" }, { value: "Outgoing", text: "Outgoing" }, { value: "Internal", text: "Internal" }], r.matchLetterType || "");
                var pri = select([{ value: "", text: t("anyPriority") }, "Urgent", "High", "Normal", "Low"].map(function (x) { return typeof x === "string" ? { value: x, text: x } : x; }), r.matchPriority || "");
                var proj = select(opts(lookups.projects, t("anyPriority")), r.matchProjectId || "");
                var disc = select(opts(lookups.disciplines, t("anyPriority")), r.matchDisciplineId || "");
                var sender = input("text", r.matchSenderContains || "", { maxlength: "120" });
                var subject = input("text", r.matchSubjectContains || "", { maxlength: "120" });
                var tag = select([{ value: "", text: t("none") }].concat(tags.map(function (x) { return { value: x.TagId, text: x.Name }; })), r.applyTagId || "");
                var star = el("input", { type: "checkbox", class: "form-check-input", id: "ctRuleStar" }); star.checked = !!r.starLetter;
                var referTo = select([{ value: "", text: t("none") }], r.referToEmployeeId || "");
                var referNote = input("text", r.referInstruction || "", { maxlength: "300" });
                // ارجاع فقط به زیردستان سازمانی: همان فهرست «تفویض»، فیلترشده روی نسبت زیردست
                C.get(C.cfg.urls.delegations).then(function (d) {
                    d.candidates.filter(function (c) { return c.relation === "Subordinate"; }).forEach(function (c) {
                        var op = el("option", { value: String(c.employeeId), text: c.name + (c.position ? " — " + c.position : "") }); if (String(c.employeeId) === String(r.referToEmployeeId || "")) op.selected = true; referTo.appendChild(op);
                    });
                }).catch(function () { /* the field stays "none" */ });
                host.appendChild(el("div", {}, [
                    el("div", { class: "ct-form-row" }, [field(t("ruleName"), name, "flex-grow-1")]),
                    el("div", { class: "fw-semibold small mb-1", text: t("ifLabel") }),
                    el("div", { class: "ct-form-row" }, [field(t("type"), type), field(t("priority"), pri), field(t("project"), proj), field(t("discipline"), disc)]),
                    el("div", { class: "ct-form-row" }, [field(t("senderContains"), sender, "flex-grow-1"), field(t("subjectContains"), subject, "flex-grow-1")]),
                    el("div", { class: "fw-semibold small mb-1", text: t("thenLabel") }),
                    el("div", { class: "ct-form-row" }, [field(t("applyTag"), tag), el("div", { class: "form-check mb-1" }, [star, el("label", { class: "form-check-label", for: "ctRuleStar", text: t("starIt") })]),
                        field(t("referTo") + " (" + t("subordinatesOnly") + ")", referTo, "flex-grow-1"), field(t("referNote"), referNote, "flex-grow-1")]),
                    button(t("save"), "btn-pems-solid", function () {
                        run(C.post(C.cfg.urls.ruleSave, {
                            letterRoutingRuleId: r.letterRoutingRuleId || 0, name: name.value, isActive: r.isActive !== false,
                            matchLetterType: type.value || null, matchPriority: pri.value || null, matchProjectId: proj.value ? parseInt(proj.value, 10) : null,
                            matchDisciplineId: disc.value ? parseInt(disc.value, 10) : null, matchSenderContains: sender.value || null, matchSubjectContains: subject.value || null,
                            applyTagId: tag.value ? parseInt(tag.value, 10) : null, starLetter: star.checked, referToEmployeeId: referTo.value ? parseInt(referTo.value, 10) : null,
                            referInstruction: referNote.value || null
                        }), reload);
                    }), button(t("cancel"), "btn-link", reload)
                ]));
            }
            list();
        }).catch(function () { clear(host); host.appendChild(note(t("failed"))); });
    }

    // ------------------------------------------------------------------ contacts + groups
    function renderContacts() {
        var pane = body("contacts"); clear(pane);
        var host = el("div"); pane.appendChild(host);
        function load() {
            C.get(C.cfg.urls.groups).then(function (groups) {
                clear(host);
                var ul = el("ul", { class: "ct-list" });
                if (!groups.length) ul.appendChild(el("li", {}, [note(t("noItems"))]));
                groups.forEach(function (g) {
                    var li = el("li", {}, [el("div", { class: "ct-grow" }, [el("div", { class: "fw-semibold", text: g.name }),
                        el("div", { class: "ct-sub", text: g.members.map(function (m) { return m.name; }).join("، ") })]),
                        el("span", { class: "ct-badge-rel", text: g.isPersonal ? t("personal") : t("organizational") })]);
                    if (g.canEdit) {
                        li.appendChild(button(t("edit"), "btn-outline-secondary", function () { edit(g); }));
                        li.appendChild(button(t("delete"), "btn-outline-danger", function () { if (window.confirm(t("confirmDelete"))) run(C.post(C.cfg.urls.groupDelete, { id: g.letterContactGroupId }), load); }));
                    }
                    ul.appendChild(li);
                });
                host.appendChild(ul);
                host.appendChild(el("div", { class: "mt-3" }, [button(t("addGroup"), "btn-pems-solid", function () { edit(null); })]));
            }).catch(function () { clear(host); host.appendChild(note(t("failed"))); });
        }
        function edit(g) {
            clear(host);
            var name = input("text", g ? g.name : "", { maxlength: "120" });
            var scope = select([{ value: "personal", text: t("scopePersonal") }].concat(C.cfg.canManageOrg ? [{ value: "org", text: t("scopeOrg") }] : []), g ? (g.isPersonal ? "personal" : "org") : "personal");
            if (g) scope.disabled = true;
            var chosen = {};   // key "E:id" / "P:id" -> {kind,id,name}
            (g ? g.members : []).forEach(function (m) { var k = m.employeeId ? "E:" + m.employeeId : "P:" + m.correspondentPartyId; chosen[k] = { kind: m.employeeId ? "Employee" : "Party", id: m.employeeId || m.correspondentPartyId, name: m.name }; });
            var chips = el("div", { class: "d-flex flex-wrap gap-1 mb-2" });
            var results = el("ul", { class: "ct-list mb-2", style: "max-height:180px;overflow:auto" });
            var q = input("search", "", { placeholder: t("searchContacts") });
            function drawChips() {
                clear(chips);
                Object.keys(chosen).forEach(function (k) {
                    chips.appendChild(el("span", { class: "badge text-bg-secondary d-inline-flex align-items-center gap-1" }, [chosen[k].name,
                        el("button", { type: "button", class: "btn-close btn-close-white", style: "font-size:.55rem", "aria-label": t("remove"), onclick: function () { delete chosen[k]; drawChips(); } })]));
                });
                if (!Object.keys(chosen).length) chips.appendChild(el("span", { class: "ct-sub", text: t("members") + ": 0" }));
            }
            var timer = null;
            function search() {
                C.get(C.cfg.urls.contacts + "?q=" + encodeURIComponent(q.value)).then(function (items) {
                    clear(results);
                    items.forEach(function (c) {
                        var key = (c.kind === "Employee" ? "E:" : "P:") + c.id;
                        results.appendChild(el("li", { style: "cursor:pointer", onclick: function () { chosen[key] = c; drawChips(); } }, [
                            el("span", { class: "ct-badge-rel", text: c.kind === "Employee" ? t("employee") : t("party") }), el("span", { class: "ct-grow", text: c.name }), el("span", { class: "ct-sub", text: c.subtitle || "" })]));
                    });
                });
            }
            q.addEventListener("input", function () { clearTimeout(timer); timer = setTimeout(search, 250); });
            host.appendChild(el("div", { class: "ct-form-row" }, [field(t("groupName"), name, "flex-grow-1"), field("", scope)]));
            host.appendChild(el("div", { class: "form-label small", text: t("members") }));
            host.appendChild(chips); host.appendChild(field(t("searchContacts"), q)); host.appendChild(results);
            host.appendChild(button(t("save"), "btn-pems-solid", function () {
                var vals = Object.keys(chosen).map(function (k) { return chosen[k]; });
                run(C.post(C.cfg.urls.groupSave, {
                    groupId: g ? g.letterContactGroupId : null, name: name.value, personal: scope.value === "personal",
                    employeeIds: vals.filter(function (v) { return v.kind === "Employee"; }).map(function (v) { return v.id; }),
                    partyIds: vals.filter(function (v) { return v.kind === "Party"; }).map(function (v) { return v.id; })
                }), load);
            }));
            host.appendChild(button(t("cancel"), "btn-link", load));
            drawChips(); search();
        }
        load();
    }

    // ------------------------------------------------------------------ reminders
    function renderReminders() {
        var pane = body("reminders"); clear(pane);
        var host = el("div"); pane.appendChild(host);
        function load() {
            C.get(C.cfg.urls.reminders).then(function (items) {
                clear(host);
                var ul = el("ul", { class: "ct-list" });
                if (!items.length) ul.appendChild(el("li", {}, [note(t("noItems"))]));
                items.forEach(function (r) {
                    ul.appendChild(el("li", {}, [el("div", { class: "ct-grow" }, [el("div", {}, [el("strong", { text: r.letterNumber }), " ", r.subject]),
                        el("div", { class: "ct-sub", text: String(r.remindAt).replace("T", " ").slice(0, 16) + (r.note ? " · " + r.note : "") })]),
                        r.isDue ? el("span", { class: "ct-badge-rel", text: t("due") }) : null,
                        el("a", { class: "btn btn-sm btn-outline-secondary", href: "/Letter/Details/" + r.letterId, text: t("open") }),
                        button(t("done"), "btn-outline-secondary", function () { run(C.post(C.cfg.urls.reminderComplete, { id: r.letterReminderId }), load); }),
                        button(t("delete"), "btn-outline-danger", function () { run(C.post(C.cfg.urls.reminderDelete, { id: r.letterReminderId }), load); })]));
                });
                host.appendChild(ul);
            }).catch(function () { clear(host); host.appendChild(note(t("failed"))); });
        }
        load();
    }

    var renderers = { tags: renderTags, delegation: renderDelegation, rules: renderRules, contacts: renderContacts, reminders: renderReminders };
})();
