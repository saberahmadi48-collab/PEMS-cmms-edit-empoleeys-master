// PEMS Organization Chart: pure, DOM-free helpers (staffing summary, vacancy filter, discipline search).
// Kept separate from pems-org-chart.js so they can be unit-tested under Node and reused without the page.
// Tree shape (Team/OrganizationTree): groups[] -> { children: group[], teams: team[] };
//                                      team       -> { children: team[], positions: position[] }.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsOrgChartLogic = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    // ---- walking ----

    function walkTeams(team, onPosition) {
        (team.positions || []).forEach(onPosition);
        (team.children || []).forEach(function (child) { walkTeams(child, onPosition); });
    }

    function walkGroups(groups, onPosition) {
        (groups || []).forEach(function (group) {
            walkGroups(group.children, onPosition);
            (group.teams || []).forEach(function (team) { walkTeams(team, onPosition); });
        });
    }

    // ---- staffing summary ----
    // Only ACTIVE positions are counted (inactive ones are drawn dimmed and are neither filled nor vacant).
    // A position that is reachable through more than one path is counted once (by PositionId when present).

    function summarize(tree) {
        var seen = Object.create(null);
        var total = 0, vacant = 0;
        walkGroups(tree, function (position) {
            if (!position || position.isActive === false) return;
            if (position.positionId != null) {
                if (seen[position.positionId]) return;
                seen[position.positionId] = true;
            }
            total++;
            if (position.isVacant) vacant++;
        });
        var filled = total - vacant;
        return {
            total: total,
            filled: filled,
            vacant: vacant,
            occupancyPercent: total === 0 ? 0 : Math.round((filled * 100) / total)
        };
    }

    // ---- vacancy filter ----
    // mode: "all" | "filled" | "vacant". Returns a pruned COPY: positions that do not match (and inactive ones) are
    // dropped, and teams/groups that end up with no matching position anywhere below them are dropped too, so the
    // existing renderer draws a correct tree (connectors depend on real first/last children, so nodes are removed
    // from the data instead of being hidden with CSS).

    function pruneTeam(team, wantVacant) {
        var children = (team.children || []).map(function (c) { return pruneTeam(c, wantVacant); }).filter(Boolean);
        var positions = (team.positions || []).filter(function (p) {
            return p && p.isActive !== false && (wantVacant ? !!p.isVacant : !p.isVacant);
        });
        if (!children.length && !positions.length) return null;
        return Object.assign({}, team, { children: children, positions: positions });
    }

    function pruneGroup(group, wantVacant) {
        var children = (group.children || []).map(function (c) { return pruneGroup(c, wantVacant); }).filter(Boolean);
        var teams = (group.teams || []).map(function (t) { return pruneTeam(t, wantVacant); }).filter(Boolean);
        if (!children.length && !teams.length) return null;
        return Object.assign({}, group, { children: children, teams: teams });
    }

    function filterTree(tree, mode) {
        if (mode !== "filled" && mode !== "vacant") return tree || [];
        var wantVacant = mode === "vacant";
        return (tree || []).map(function (g) { return pruneGroup(g, wantVacant); }).filter(Boolean);
    }

    // ---- discipline search ----
    // Persian text is typed with either Arabic or Persian letter forms and sometimes with ZWNJ; compare on a
    // normalised form so "کنترل" finds "كنترل" and "ابزاردقیق" finds "ابزار‌دقیق".

    function normalize(text) {
        return String(text == null ? "" : text)
            .replace(/ي|ى/g, "ی")   // Arabic yeh / alef maksura -> Persian yeh
            .replace(/ك/g, "ک")          // Arabic kaf -> Persian kaf
            .replace(/[‌​‍ـ]/g, "")   // ZWNJ, ZWSP, ZWJ, tatweel
            .replace(/\s+/g, " ")
            .trim()
            .toLowerCase();
    }

    // options: [{ value, text }] (without the "all" placeholder). The currently selected option is always kept so the
    // select never silently changes its value while the user types.
    function filterOptions(options, query, selectedValue) {
        var q = normalize(query);
        if (!q) return options.slice();
        return options.filter(function (o) {
            return String(o.value) === String(selectedValue) || normalize(o.text).indexOf(q) !== -1;
        });
    }

    return { summarize: summarize, filterTree: filterTree, normalize: normalize, filterOptions: filterOptions };
});
