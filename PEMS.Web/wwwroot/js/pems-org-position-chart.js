// Organization Chart (Position hierarchy). Read-only: loads one JSON DTO from Admin/Organization/ChartData and hands it to the
// vendored d3-org-chart library (layout, zoom, pan, expand/collapse are all the library's). No data is ever written from here.
(function () {
    "use strict";
    var root = document.getElementById("pemsOpcRoot");
    if (!root || !window.d3 || !d3.OrgChart) return;

    var T = function (k) { return root.getAttribute("data-t-" + k) || k; };
    var canvas = document.getElementById("pemsOpcCanvas");
    var stateEl = document.getElementById("pemsOpcState");
    var panelEl = document.getElementById("pemsOpcPanel");
    var warnEl = document.getElementById("pemsOpcWarnings");
    var chart = null, byId = {}, nodes = [], selectedId = null;

    // markup comes from the page's registered templates (script-templates component, pems-template.js), never from a string in this file
    var Tpl = window.PemsTemplate;
    function tpl(name, values) { return Tpl.fill(Tpl.clone(name), values || {}); }
    function showState(msg) { stateEl.textContent = msg; stateEl.hidden = false; canvas.hidden = true; }
    function hideState() { stateEl.hidden = true; canvas.hidden = false; }
    // SCRUM-337: the shared skeleton while the chart data loads
    function showLoading() { window.PemsSkeleton.show(stateEl, "card", { label: T("loading") }); stateEl.hidden = false; canvas.hidden = true; }

    // d3-org-chart takes the node content as a markup string: it is the serialisation of a cloned template, not hand-built text
    function nodeHtml(d) {
        var n = d.data || d;
        if (n.nodeKind === "container") return tpl("opc-node-container", { text: T("container") + " (" + n.childCount + ")" }).outerHTML;
        var hasIssue = n.nodeKind === "orphan" || n.nodeKind === "cycle" || (n.issueCodes && n.issueCodes.length);
        var title = n.positionTitle == null ? "" : n.positionTitle;
        var el = tpl("opc-node", {
            root: { addClass: "kind-" + n.nodeKind + (n.isVacant ? " is-vacant" : "") + (n.id === selectedId ? " is-selected" : ""), attrs: { "aria-label": title } },
            label: T("position"), title: title,
            vacant: n.isVacant ? T("vacant") : null,
            emp: n.isVacant ? null : (n.employeeName == null ? "" : n.employeeName),
            unit: n.departmentName || n.teamName || "",
            badge: hasIssue ? { text: "⚠ " + T(n.nodeKind === "cycle" ? "cycle" : n.nodeKind === "orphan" ? "orphan" : "issue"), attrs: { title: (n.issueCodes || []).join(", ") } } : null
        });
        return el.outerHTML;
    }

    function showDetails(n) {
        if (!n || n.nodeKind === "container") { panelEl.replaceChildren(tpl("opc-hint", { text: T("select") })); return; }
        var parent = n.parentId && byId[n.parentId] && byId[n.parentId].nodeKind !== "container" ? byId[n.parentId] : null;
        var reports = nodes.filter(function (x) { return x.parentId === n.id; });
        var kind = { root: T("validRoot"), position: "", orphan: T("orphan"), cycle: T("cycle") }[n.nodeKind] || "";
        var f = Tpl.fill(Tpl.fragment("opc-detail"), {
            title: n.positionTitle == null ? "" : n.positionTitle,
            code: n.positionCode ? "[" + n.positionCode + "]" : null,
            manage: { text: T("manage"), attrs: { href: root.getAttribute("data-manage-url") } }
        });
        var dl = f.querySelector("dl");
        function row(term, value) {
            var r = Tpl.fill(Tpl.fragment("opc-detail-row"), { term: term });
            var dd = r.querySelector("dd");
            dd.replaceChildren.apply(dd, [].concat(value));
            dl.appendChild(r);
        }
        row(T("employee"), n.isVacant ? T("vacant") : n.employeeName + (n.holderIsPrimary ? "" : " (" + T("secondary") + ")"));
        if (n.assignmentFrom) row(T("assignment"), n.assignmentFrom + " → " + (n.assignmentTo || "—"));
        row(T("unit"), n.departmentName || "—");
        if (n.teamName) row(T("team"), n.teamName);
        row(T("parent"), [parent ? parent.positionTitle : "—"].concat(kind ? [" ", tpl("opc-note", { text: "(" + kind + ")" })] : []));
        var ul = null;
        if (reports.length) {
            ul = tpl("opc-reports");
            reports.forEach(function (r) { ul.appendChild(tpl("opc-report-item", { text: r.positionTitle + " — " + (r.isVacant ? T("vacant") : r.employeeName) })); });
        }
        row(T("reports") + " (" + reports.length + ")", ul || "—");
        if (n.issueCodes && n.issueCodes.length) row(T("issues"), n.issueCodes.join(", "));
        panelEl.replaceChildren(f);
    }

    function select(id) {
        selectedId = id;
        showDetails(byId[id]);
        if (chart) chart.render();
    }

    function build(dto) {
        nodes = dto.nodes || [];
        byId = {}; nodes.forEach(function (n) { byId[n.id] = n; });
        if (!nodes.length) { showState(T("empty")); return; }
        hideState();

        var warns = (dto.warnings || []).map(function (w) { return T(w.code === "HIERARCHY_CYCLE" ? "cycle" : "orphan") + ": " + w.count; });
        warnEl.hidden = warns.length === 0;
        warnEl.textContent = warns.join(" · ");

        chart = new d3.OrgChart()
            .container("#pemsOpcCanvas")
            .data(nodes.map(function (n) { return Object.assign({}, n, { parentId: n.parentId || null }); }))
            .svgHeight(canvas.clientHeight || 520)
            .nodeWidth(function (d) { return (d.data.nodeKind === "container") ? 150 : 230; })
            .nodeHeight(function (d) { return (d.data.nodeKind === "container") ? 44 : 118; })
            .childrenMargin(function () { return 50; })
            .compactMarginBetween(function () { return 24; })
            .compactMarginPair(function () { return 40; })
            .neighbourMargin(function () { return 24; })
            .initialExpandLevel(2)
            .nodeContent(nodeHtml)
            .buttonContent(function (o) {
                var n = o.node;
                return tpl("opc-button", { text: (n.children ? "−" : "+") + " " + (n.data._directSubordinates || "") }).outerHTML;
            })
            .onNodeClick(function (d) { var n = d.data || d; select(n.id); })
            .render();
        chart.fit();
    }

    function bind(id, fn) { var el = document.getElementById(id); if (el) el.addEventListener("click", fn); }
    bind("pemsOpcFit", function () { if (chart) chart.fit(); });
    bind("pemsOpcZoomIn", function () { if (chart) chart.zoomIn(); });
    bind("pemsOpcZoomOut", function () { if (chart) chart.zoomOut(); });
    bind("pemsOpcExpand", function () { if (chart) { chart.expandAll(); chart.fit(); } });
    bind("pemsOpcCollapse", function () { if (chart) { chart.collapseAll(); chart.setExpanded(nodes[0].id, true).render(); chart.fit(); } });

    var search = document.getElementById("pemsOpcSearch");
    if (search) search.addEventListener("keydown", function (e) {
        if (e.key !== "Enter" || !chart) return;
        var q = search.value.trim().toLowerCase();
        if (!q) { chart.clearHighlighting(); return; }
        var hit = nodes.find(function (n) {
            return n.nodeKind !== "container" && ((n.positionTitle || "").toLowerCase().indexOf(q) >= 0 || (n.employeeName || "").toLowerCase().indexOf(q) >= 0 ||
                (n.departmentName || "").toLowerCase().indexOf(q) >= 0 || (n.positionCode || "").toLowerCase().indexOf(q) >= 0);
        });
        if (hit) { chart.clearHighlighting(); chart.setHighlighted(hit.id).render(); chart.setCentered(hit.id).render(); select(hit.id); }
        else search.classList.add("is-invalid");
    });
    if (search) search.addEventListener("input", function () { search.classList.remove("is-invalid"); });

    showLoading();
    fetch(root.getAttribute("data-url"), { credentials: "same-origin", headers: { Accept: "application/json" } })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .then(build)
        .catch(function () { showState(T("error")); });
})();
