# ADR-0002: Razor with a separate PEMS.UI component library

- **Status:** Accepted
- **Date:** 2026-10-18 (refined 2026-10-23, see Amendments)
- **Decided by:** Hamid Maleki, project owner and Member A (plan row #5, "Confirmed by PM: Yes")
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #5 (decision); #319 (component technology spike); #320-#334 and #29-#43 (library work); written under #2 (SCRUM-14)
- **Sources:** `plan/rows/0005.md`; [COMPONENT_LIBRARY_PLAN.md](../../docs/design/COMPONENT_LIBRARY_PLAN.md) section 4; Instructions rule 26

## Context

The UI is Razor + Bootstrap + jQuery with 225 views at the time of the decision and page-specific JS/CSS (for example `pems-data-grid.js`).
The static audit of 2026-10-23 counts 339 views, 0 View Components, 0 Tag Helpers, 2,587 hand-written inputs against 202 model-bound
fields and 342 hand-written tables. A design language exists (`.vs-*`, `--pems-*` tokens) but is applied by copy-paste.
The future dynamic BPMS renders forms and views from metadata at runtime and must use the same components as static pages.
The product must work fully offline (no CDN), in RTL, and be upgradable from the existing Razor pages.

## Decision

1. **Keep Razor.** Build a reusable component library; the form and workflow designers are embedded JavaScript components (decided 2026-10-18, D1).
2. The library is a **separate Razor Class Library, `PEMS.UI`** (components, tokens, CSS/JS, localization, catalog, tests); `PEMS.Web` only consumes it (decided 2026-10-23, D2).
3. Pilot on the Admin area (plus account pages and the shared shell) after the whole-project audit (decided 2026-10-23, D4).
4. Once `PEMS.UI` exists, new pages are built from its components and templates; raw form-control, table, modal and badge markup, inline styles, inline scripts and CDN references are not added, enforced by a CI ratchet (Instructions rule 26, 2026-10-23).

**Not yet decided:** the canonical component technology (D3). The recommendation is descriptor-driven static Razor components with a thin
MVC Tag Helper facade over a technology-neutral contract, gated by spike #319. This ADR does not accept that recommendation; the spike
outcome gets its own ADR.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| ViewComponent / TagHelper plus Web Components | Compared as option A in the plan: Tag Helpers are compile-time markup and cannot be chosen by metadata at runtime; remains the fallback for failing component classes if the spike fails. |
| Blazor interactive or an SPA hosted in the app | Option C: would rewrite everything, needs server circuits or large downloads, costs offline and RTL effort, and contradicts keeping Razor. |
| Components inside `PEMS.Web` | Replaced by the separate `PEMS.UI` library (D2). |

## Consequences

- Good: one component vocabulary for static pages and the dynamic BPMS; tokens and logical CSS give RTL from the same markup; a CI ratchet stops new raw markup.
- Cost: large migration (waves W1-W5 over about 300 pages); the vertical-slice comparison that row #5 asked for was never run, which is why the technology choice is gated by #319.
- All CMP_* and FRM_* work is defined against this ADR (Requirement of row #5).
- Follow-up: spike #319, contract #321, pilot #328, waves #329-#332.

## Amendments

- 2026-10-23: decision refined in `COMPONENT_LIBRARY_PLAN.md` (separate RCL `PEMS.UI`; rule 26).
