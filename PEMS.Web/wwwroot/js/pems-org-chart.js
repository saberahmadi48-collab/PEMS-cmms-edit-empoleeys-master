// PEMS Dynamic Maintenance Organization — Phase 6A: Graphical Organization Chart.
// طبق سند: فقط visualize -> select -> inspect -> navigate. بدون Drag & Drop، بدون جهش
// ساختاری از این نمودار. داده فقط از Team/OrganizationTree (JSON، خواندنی) خوانده می‌شود.
(function () {
    "use strict";

    var root = document.getElementById("pemsOrgChartRoot");
    if (!root) return;

    var treeUrl = root.getAttribute("data-tree-url");
    var detailsUrlTemplate = root.getAttribute("data-details-url-template");
    var emptyText = root.getAttribute("data-empty-text");
    var errorText = root.getAttribute("data-error-text");
    var vacantText = root.getAttribute("data-vacant-text");
    var positionsText = root.getAttribute("data-positions-text");
    var membersText = root.getAttribute("data-members-text");
    var viewDetailsText = root.getAttribute("data-view-details-text");
    var filterEmptyText = root.getAttribute("data-filter-empty-text");
    var noDisciplineText = root.getAttribute("data-no-discipline-text");
    var allDisciplinesText = root.getAttribute("data-all-disciplines-text");

    // Pure helpers (summary, vacancy filter, discipline search) live in pems-org-chart-logic.js
    var Logic = window.PemsOrgChartLogic;

    var loadingEl = document.getElementById("orgChartLoading");
    var errorEl = document.getElementById("orgChartError");
    var errorTextEl = document.getElementById("orgChartErrorText");
    var emptyEl = document.getElementById("orgChartEmpty");
    var emptyTextEl = document.getElementById("orgChartEmptyText");
    var viewportEl = document.getElementById("orgChartViewport");
    var canvasEl = document.getElementById("orgChartCanvas");
    var retryBtn = document.getElementById("orgChartRetry");
    var disciplineFilter = document.getElementById("orgChartDisciplineFilter");
    var disciplineSearch = document.getElementById("orgChartDisciplineSearch");
    var summaryEl = document.getElementById("orgChartSummary");

    var noSelectionEl = document.getElementById("orgChartNoSelection");
    var detailContentEl = document.getElementById("orgChartDetailContent");

    var rawTree = [];          // last tree from the server (unfiltered); the vacancy filter redraws from this
    var filterMode = "all";    // all | filled | vacant

    var zoom = 1;
    var MIN_ZOOM = 0.4, MAX_ZOOM = 2, ZOOM_STEP = 0.15;

    // keepSummary: the staffing summary stays visible when the chart is empty only because of the vacancy filter
    function showState(state, keepSummary) {
        loadingEl.hidden = state !== "loading";
        errorEl.hidden = state !== "error";
        emptyEl.hidden = state !== "empty";
        viewportEl.hidden = state !== "ready";
        if (summaryEl) summaryEl.hidden = !(state === "ready" || keepSummary === true);
    }

    function esc(s) {
        var d = document.createElement("div");
        d.textContent = s == null ? "" : String(s);
        return d.innerHTML;
    }

    // ---- Node kind → icon/label (kind comes verbatim from Team.NodeKind; never inferred from name) ----
    function teamKindIcon(nodeKind) {
        switch (nodeKind) {
            case "Shift": return "bi-clock-history";
            case "SubTeam": return "bi-diagram-2";
            case "Unit": return "bi-hdd-network";
            default: return "bi-people-fill";
        }
    }

    function buildGroupNode(group) {
        var li = document.createElement("li");
        var hasChildren = (group.children && group.children.length) || (group.teams && group.teams.length);

        li.appendChild(buildToggle(hasChildren));

        var node = document.createElement("div");
        node.className = "pems-org-node";
        node.setAttribute("data-kind", "Group");
        node.setAttribute("tabindex", "-1");
        node.setAttribute("role", "treeitem");
        if (!group.isActive) node.setAttribute("data-inactive", "true");
        node.innerHTML =
            '<span class="pems-org-node-kind"><i class="bi bi-diagram-3"></i>' + esc(groupKindLabel(group)) + '</span>' +
            '<span class="pems-org-node-name">' + esc(group.name) + '</span>' +
            '<span class="pems-org-node-meta">' + esc(group.code) + (group.disciplineName ? (' &middot; ' + esc(group.disciplineName)) : '') + '</span>';

        node.addEventListener("click", function () { selectGroup(group, node); });
        node.addEventListener("keydown", onNodeKeydown);
        li.appendChild(node);

        if (hasChildren) {
            var ul = document.createElement("ul");
            ul.setAttribute("role", "group");
            (group.children || []).forEach(function (child) { ul.appendChild(buildGroupNode(child)); });
            (group.teams || []).forEach(function (team) { ul.appendChild(buildTeamNode(team)); });
            li.appendChild(ul);
        } else {
            li.classList.add("pems-org-leaf");
        }

        return li;
    }

    function groupKindLabel(group) {
        // طبق بند ۳ سند: تمایز بصری Group/Sub-Group - بدون فیلد جداگانه‌ی سرور، از وجود
        // ParentMaintenanceGroupId استفاده می‌شود (خودِ ساختار داده، نه حدس از روی نام).
        return group.parentMaintenanceGroupId ? "Sub-Group" : "Maintenance Group";
    }

    function buildTeamNode(team) {
        var li = document.createElement("li");
        var hasChildren = (team.children && team.children.length) || (team.positions && team.positions.length);

        li.appendChild(buildToggle(hasChildren));

        var node = document.createElement("div");
        node.className = "pems-org-node";
        node.setAttribute("data-kind", team.nodeKind || "Team");
        node.setAttribute("tabindex", "-1");
        node.setAttribute("role", "treeitem");
        if (!team.isActive) node.setAttribute("data-inactive", "true");

        var vacancyBadge = team.vacantPositionCount > 0
            ? ('<span class="pems-org-badge-vacant"><i class="bi bi-exclamation-circle"></i> ' + team.vacantPositionCount + '</span>')
            : '';

        node.innerHTML =
            '<span class="pems-org-node-kind"><i class="bi ' + teamKindIcon(team.nodeKind) + '"></i>' + esc(team.nodeKind || "Team") + '</span>' +
            '<span class="pems-org-node-name">' + esc(team.name) + '</span>' +
            '<span class="pems-org-node-meta">' +
                '<span>' + membersText + ': ' + (team.memberCount || 0) + '</span>' +
                '<span>' + positionsText + ': ' + (team.positionCount || 0) + '</span>' +
                vacancyBadge +
            '</span>';

        node.addEventListener("click", function () { selectTeam(team, node); });
        node.addEventListener("keydown", onNodeKeydown);
        li.appendChild(node);

        if (hasChildren) {
            var ul = document.createElement("ul");
            ul.setAttribute("role", "group");
            (team.children || []).forEach(function (child) { ul.appendChild(buildTeamNode(child)); });
            (team.positions || []).forEach(function (position) { ul.appendChild(buildPositionNode(position)); });
            li.appendChild(ul);
        } else {
            li.classList.add("pems-org-leaf");
        }

        return li;
    }

    function buildPositionNode(position) {
        var li = document.createElement("li");
        li.classList.add("pems-org-leaf");

        var node = document.createElement("div");
        node.className = "pems-org-node";
        node.setAttribute("data-kind", position.isVacant ? "Position-Vacant" : "Position-Occupied");
        node.setAttribute("tabindex", "-1");
        node.setAttribute("role", "treeitem");
        if (!position.isActive) node.setAttribute("data-inactive", "true");

        var occupant = position.isVacant
            ? ('<span class="pems-org-badge-vacant"><i class="bi bi-person-dash"></i> ' + vacantText + '</span>')
            : ('<span><i class="bi bi-person-check"></i> ' + esc(position.currentAssignment.employeeName) + '</span>');

        node.innerHTML =
            '<span class="pems-org-node-kind"><i class="bi bi-person-badge"></i>Position</span>' +
            '<span class="pems-org-node-name">' + esc(position.title) + '</span>' +
            '<span class="pems-org-node-meta">' + occupant + '</span>';

        node.addEventListener("click", function () { selectPosition(position, node); });
        node.addEventListener("keydown", onNodeKeydown);
        li.appendChild(node);
        return li;
    }

    function buildToggle(hasChildren) {
        var span = document.createElement("span");
        if (!hasChildren) return span;
        span.className = "pems-org-toggle";
        span.innerHTML = '<i class="bi bi-dash"></i>';
        span.setAttribute("aria-label", "Toggle");
        span.addEventListener("click", function (e) {
            e.stopPropagation();
            var li = span.closest("li");
            li.classList.toggle("pems-org-children-hidden");
            var icon = span.querySelector("i");
            icon.className = li.classList.contains("pems-org-children-hidden") ? "bi bi-plus" : "bi bi-dash";
        });
        return span;
    }

    // ---- Selection / detail panel ----

    function clearSelection() {
        var prev = canvasEl.querySelector(".pems-org-node-selected");
        if (prev) prev.classList.remove("pems-org-node-selected");
    }

    function markSelected(node) {
        clearSelection();
        node.classList.add("pems-org-node-selected");
        node.focus();
    }

    function showDetail(rowsHtml) {
        noSelectionEl.hidden = true;
        detailContentEl.hidden = false;
        detailContentEl.innerHTML = rowsHtml;
    }

    function selectGroup(group, node) {
        markSelected(node);
        showDetail(
            '<h6 class="mb-1">' + esc(group.name) + '</h6>' +
            '<div class="text-muted small mb-2">' + esc(groupKindLabel(group)) + '</div>' +
            '<dl>' +
                '<dt>Code</dt><dd>' + esc(group.code) + '</dd>' +
                '<dt>Discipline</dt><dd>' + esc(group.disciplineName || "—") + '</dd>' +
                '<dt>Status</dt><dd>' + (group.isActive ? "Active" : "Inactive") + '</dd>' +
                '<dt>Sub-groups</dt><dd>' + ((group.children || []).length) + '</dd>' +
                '<dt>Teams</dt><dd>' + ((group.teams || []).length) + '</dd>' +
            '</dl>'
        );
    }

    function selectTeam(team, node) {
        markSelected(node);
        // طبق «PEMS Dynamic Maintenance Organization — Phase 6B» بند ۳: بعد از افزوده‌شدن
        // صفحه‌ی جزئیات واقعی (Team/TeamDetails)، این لینک به آن‌جا می‌رود، نه صرفاً
        // Highlight کردن کارت در Team/Index (که هنوز به‌عنوان راه جایگزین کار می‌کند).
        var detailsUrl = detailsUrlTemplate + "?teamId=" + encodeURIComponent(team.teamId);
        showDetail(
            '<h6 class="mb-1">' + esc(team.name) + '</h6>' +
            '<div class="text-muted small mb-2">' + esc(team.nodeKind || "Team") + '</div>' +
            '<dl>' +
                '<dt>Code</dt><dd>' + esc(team.code) + '</dd>' +
                '<dt>Parent</dt><dd>' + (team.parentTeamId ? ("#" + team.parentTeamId) : "—") + '</dd>' +
                '<dt>Status</dt><dd>' + (team.isActive ? "Active" : "Inactive") + '</dd>' +
                '<dt>Supervisor</dt><dd>' + esc(team.supervisorName || "—") + '</dd>' +
                '<dt>' + membersText + '</dt><dd>' + (team.memberCount || 0) + '</dd>' +
                '<dt>' + positionsText + '</dt><dd>' + (team.positionCount || 0) +
                    (team.vacantPositionCount ? (' <span class="pems-org-badge-vacant">(' + team.vacantPositionCount + ' ' + vacantText + ')</span>') : '') + '</dd>' +
                '<dt>Child nodes</dt><dd>' + ((team.children || []).length) + '</dd>' +
            '</dl>' +
            '<a class="btn btn-pems-solid btn-sm w-100 mt-2" href="' + detailsUrl + '">' +
                '<i class="bi bi-box-arrow-up-right"></i> ' + viewDetailsText +
            '</a>'
        );
    }

    function selectPosition(position, node) {
        markSelected(node);
        var occupantRow = position.isVacant
            ? ('<dt>Occupant</dt><dd><span class="pems-org-badge-vacant">' + vacantText + '</span></dd>')
            : ('<dt>Occupant</dt><dd>' + esc(position.currentAssignment.employeeName) + '</dd>' +
               '<dt>Since</dt><dd>' + esc(new Date(position.currentAssignment.startDate).toLocaleDateString()) + '</dd>');

        showDetail(
            '<h6 class="mb-1">' + esc(position.title) + '</h6>' +
            '<div class="text-muted small mb-2">Position</div>' +
            '<dl>' +
                '<dt>Status</dt><dd>' + (position.isActive ? "Active" : "Inactive") + '</dd>' +
                occupantRow +
            '</dl>'
        );
    }

    // ---- Keyboard navigation (roving focus across visible nodes) ----

    function visibleNodes() {
        return Array.prototype.slice.call(canvasEl.querySelectorAll(".pems-org-node"))
            .filter(function (n) { return n.offsetParent !== null; });
    }

    function onNodeKeydown(e) {
        var nodes = visibleNodes();
        var idx = nodes.indexOf(e.currentTarget);
        if (idx === -1) return;

        if (e.key === "ArrowDown") { e.preventDefault(); if (nodes[idx + 1]) nodes[idx + 1].focus(); }
        else if (e.key === "ArrowUp") { e.preventDefault(); if (nodes[idx - 1]) nodes[idx - 1].focus(); }
        else if (e.key === "Enter" || e.key === " ") { e.preventDefault(); e.currentTarget.click(); }
        else if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
            var li = e.currentTarget.closest("li");
            var isCollapsed = li.classList.contains("pems-org-children-hidden");
            var wantExpand = e.key === "ArrowRight";
            if (li.querySelector(":scope > ul") && isCollapsed === wantExpand) {
                var toggle = li.querySelector(":scope > .pems-org-toggle");
                if (toggle) toggle.click();
            }
        }
    }

    // ---- Expand all / collapse all ----

    document.getElementById("orgChartExpandAll").addEventListener("click", function () {
        canvasEl.querySelectorAll("li.pems-org-children-hidden").forEach(function (li) {
            li.classList.remove("pems-org-children-hidden");
            var icon = li.querySelector(":scope > .pems-org-toggle i");
            if (icon) icon.className = "bi bi-dash";
        });
    });

    document.getElementById("orgChartCollapseAll").addEventListener("click", function () {
        canvasEl.querySelectorAll("li").forEach(function (li) {
            if (li.querySelector(":scope > ul")) {
                li.classList.add("pems-org-children-hidden");
                var icon = li.querySelector(":scope > .pems-org-toggle i");
                if (icon) icon.className = "bi bi-plus";
            }
        });
    });

    // ---- Zoom / Fit ----

    function applyZoom() {
        canvasEl.style.transform = "scale(" + zoom.toFixed(2) + ")";
    }

    document.getElementById("orgChartZoomIn").addEventListener("click", function () {
        zoom = Math.min(MAX_ZOOM, zoom + ZOOM_STEP);
        applyZoom();
    });
    document.getElementById("orgChartZoomOut").addEventListener("click", function () {
        zoom = Math.max(MIN_ZOOM, zoom - ZOOM_STEP);
        applyZoom();
    });
    document.getElementById("orgChartFit").addEventListener("click", function () {
        zoom = 1;
        applyZoom();
        viewportEl.scrollLeft = (canvasEl.scrollWidth - viewportEl.clientWidth) / 2;
        viewportEl.scrollTop = 0;
    });

    viewportEl.addEventListener("wheel", function (e) {
        if (!e.ctrlKey) return; // پیمایش عادی صفحه دست‌نخورده می‌ماند؛ فقط Ctrl+Wheel بزرگ‌نمایی می‌کند
        e.preventDefault();
        zoom = e.deltaY < 0 ? Math.min(MAX_ZOOM, zoom + 0.08) : Math.max(MIN_ZOOM, zoom - 0.08);
        applyZoom();
    }, { passive: false });

    // ---- Pan (drag) ----

    var panning = false, panStartX = 0, panStartY = 0, scrollStartX = 0, scrollStartY = 0;

    viewportEl.addEventListener("mousedown", function (e) {
        if (e.target.closest(".pems-org-node") || e.target.closest(".pems-org-toggle")) return;
        panning = true;
        viewportEl.classList.add("pems-orgchart-panning");
        panStartX = e.clientX; panStartY = e.clientY;
        scrollStartX = viewportEl.scrollLeft; scrollStartY = viewportEl.scrollTop;
    });
    window.addEventListener("mousemove", function (e) {
        if (!panning) return;
        viewportEl.scrollLeft = scrollStartX - (e.clientX - panStartX);
        viewportEl.scrollTop = scrollStartY - (e.clientY - panStartY);
    });
    window.addEventListener("mouseup", function () {
        panning = false;
        viewportEl.classList.remove("pems-orgchart-panning");
    });

    // ---- Load ----

    function setText(id, value) {
        var el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function updateSummary(s) {
        setText("orgChartStatTotal", s.total);
        setText("orgChartStatFilled", s.filled);
        setText("orgChartStatVacant", s.vacant);
        setText("orgChartStatOccupancy", s.occupancyPercent + "%");
        setText("orgChartFilterFilled", s.filled);
        setText("orgChartFilterVacant", s.vacant);

        var filledPct = s.total === 0 ? 0 : (s.filled * 100) / s.total;
        var filledBar = document.getElementById("orgChartProgressFilled");
        var vacantBar = document.getElementById("orgChartProgressVacant");
        if (filledBar) filledBar.style.width = filledPct.toFixed(1) + "%";
        if (vacantBar) vacantBar.style.width = (s.total === 0 ? 0 : 100 - filledPct).toFixed(1) + "%";

        var progress = document.getElementById("orgChartProgress");
        if (progress) {
            var label = progress.getAttribute("data-base-label") || progress.getAttribute("aria-label") || "";
            progress.setAttribute("data-base-label", label);
            progress.setAttribute("aria-label", label + ": " + s.occupancyPercent + "% (" + s.filled + "/" + s.total + ")");
            progress.title = s.occupancyPercent + "%";
        }
    }

    function resetDetail() {
        detailContentEl.hidden = true;
        detailContentEl.innerHTML = "";
        noSelectionEl.hidden = false;
    }

    // Draws the tree for the current vacancy filter. Filtering prunes the data (see pems-org-chart-logic.js) rather
    // than hiding nodes, because the connector lines depend on real first/last children.
    function draw() {
        canvasEl.innerHTML = "";
        resetDetail();

        var shown = Logic.filterTree(rawTree, filterMode);
        if (shown.length === 0) {
            emptyTextEl.textContent = filterEmptyText;
            showState("empty", true);
            return;
        }

        var ul = document.createElement("ul");
        ul.className = "pems-org-tree";
        ul.setAttribute("role", "tree");
        shown.forEach(function (group) { ul.appendChild(buildGroupNode(group)); });
        canvasEl.appendChild(ul);

        showState("ready");
        zoom = 1;
        applyZoom();
    }

    function render(tree) {
        rawTree = tree || [];
        if (rawTree.length === 0) {
            canvasEl.innerHTML = "";
            emptyTextEl.textContent = emptyText;
            showState("empty");
            return;
        }
        updateSummary(Logic.summarize(rawTree));
        draw();
    }

    function load() {
        showState("loading");
        var url = treeUrl;
        var disciplineId = disciplineFilter.value;
        if (disciplineId) url += "?disciplineId=" + encodeURIComponent(disciplineId);

        fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (resp) {
                if (resp.status === 403) throw new Error("unauthorized");
                if (!resp.ok) throw new Error("http-" + resp.status);
                return resp.json();
            })
            .then(function (data) { render(data); })
            .catch(function (err) {
                errorTextEl.textContent = err && err.message === "unauthorized"
                    ? "You do not have permission to view the organization chart."
                    : errorText;
                showState("error");
            });
    }

    retryBtn.addEventListener("click", load);
    disciplineFilter.addEventListener("change", load);

    // ---- Vacancy filter (all / filled / vacant) ----

    var filterButtons = Array.prototype.slice.call(document.querySelectorAll("#orgChartVacancyFilter button"));
    filterButtons.forEach(function (btn) {
        btn.addEventListener("click", function () {
            filterMode = btn.getAttribute("data-filter");
            filterButtons.forEach(function (b) {
                var on = b === btn;
                b.classList.toggle("active", on);
                b.setAttribute("aria-pressed", on ? "true" : "false");
            });
            if (rawTree.length) draw();
        });
    });

    // ---- Searchable discipline filter ----
    // The <select> stays the source of truth (load() reads its value); the search box only narrows its options.
    // The selected option is always kept so typing never changes the active filter by itself.

    var disciplineOptions = Array.prototype.slice.call(disciplineFilter.options)
        .filter(function (o) { return o.value !== ""; })
        .map(function (o) { return { value: o.value, text: o.textContent }; });

    function addOption(value, text, disabled) {
        var o = document.createElement("option");
        o.value = value;
        o.textContent = text;
        if (disabled) o.disabled = true;
        disciplineFilter.appendChild(o);
    }

    function rebuildDisciplineOptions() {
        var selected = disciplineFilter.value;
        var query = disciplineSearch.value;
        var matches = Logic.filterOptions(disciplineOptions, query, selected);

        while (disciplineFilter.firstChild) disciplineFilter.removeChild(disciplineFilter.firstChild);
        addOption("", allDisciplinesText, false);
        matches.forEach(function (m) { addOption(m.value, m.text, false); });
        if (Logic.normalize(query) && Logic.filterOptions(disciplineOptions, query, null).length === 0)
            addOption("", noDisciplineText, true);
        disciplineFilter.value = selected;
    }

    if (disciplineSearch) {
        disciplineSearch.addEventListener("input", rebuildDisciplineOptions);
        disciplineSearch.addEventListener("keydown", function (e) {
            if (e.key === "Escape") {
                disciplineSearch.value = "";
                rebuildDisciplineOptions();
            } else if (e.key === "Enter") {
                e.preventDefault();
                var only = Logic.filterOptions(disciplineOptions, disciplineSearch.value, null);
                if (only.length === 1 && disciplineSearch.value.trim()) {
                    disciplineFilter.value = only[0].value;
                    disciplineFilter.dispatchEvent(new Event("change"));
                }
            }
        });
    }

    load();
})();
