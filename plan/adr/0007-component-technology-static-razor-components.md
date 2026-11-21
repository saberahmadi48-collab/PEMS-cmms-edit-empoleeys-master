# ADR-0007: Component technology: static Razor components with a Tag Helper facade

- **Status:** Accepted
- **Date:** 2026-10-27
- **Decided by:** Hamid Maleki (project owner, Member A), on the proposal of the SCRUM-23 spike
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 / SCRUM-23 session
- **Plan rows:** #319 (spike, SCRUM-23); unblocks #320 (SCRUM-29) and #321 (SCRUM-336); updates row #5 / ADR-0002 D3
- **Sources:** [COMPONENT_LIBRARY_PLAN.md](../../docs/design/COMPONENT_LIBRARY_PLAN.md) section 4.1; the spike code in `PEMS.UI.Abstractions`, `PEMS.UI`, `PEMS.UI.Tests`; measured by `PEMS.UI.Tests/SpikeTests.cs`

## Context

ADR-0002 decided Razor with a separate `PEMS.UI` library but left the component technology open (D3), gated by a vertical-slice spike that
row #5 had asked for and nobody ran. The dynamic BPMS needs to pick a component by name and give it parameters from data, also outside an
HTTP request (designer preview, e-mail, PDF) and from extension assemblies. The 303 existing MVC pages need a cheap migration path.

## What was built (the slice)

Standalone projects, **not referenced by `PEMS.Web`** (no page changes):

- `PEMS.UI.Abstractions`: `FieldDescriptor`, `ColumnDescriptor`, `ViewDescriptor`, `FormDefinition`, `FormState`. No Razor/MVC types.
- `PEMS.UI`: Razor components `PemsButton`, `PemsBadge`, `PemsSkeleton`, `PemsField` (text, text area, number, date, select, checkbox, file), `PemsTable`, `PemsForm`, `PemsDescriptorHost` (picks a component by key from a parameter dictionary), a component registry with `[PemsComponent("key")]` for extension packs, a request-free `IPemsComponentRenderer` (`HtmlRenderer`), the `<pems-field asp-for>` Tag Helper facade, and, as option A, the same table as a View Component.
- Each component is split into **inputs** (an immutable `*Options` record or the descriptor), **state** (a plain C# `*State` that computes CSS, ids and ARIA, testable without rendering) and **body** (the `.razor` markup that only prints).
- `PEMS.UI.Tests`: an in-process MVC host (TestServer, no browser, no database) with a field set with validation, a 1,000-row table and a form with antiforgery and file upload, rendered from an MVC view and from a descriptor.

## Results (measured on a 4-core cloud container, .NET 10.0.113, median of 25 runs after 5 warm-ups; three runs, range shown)

| Case (1,000 rows x 6 columns) | median ms | html bytes |
|---|---|---|
| B, standalone (no request), rows in one component | 4.7 to 14.4 | 269,661 |
| B, standalone, one component per cell | 34.8 to 48.0 | 269,432 |
| A, MVC request, View Component | 7.7 to 13.5 | 421,799 |
| B, MVC request, rows in one component | 6.1 to 8.9 | 269,662 |
| B, MVC request, one component per cell | 37.9 to 39.6 | 269,433 |

The run-to-run spread is large on a shared container, so read this as an order of magnitude: rows rendered inside one component are as fast as
a View Component (equal within noise) and produce about a third less HTML; a component per cell is about 5 to 8 times slower.

| Criterion | B: static Razor components + Tag Helper facade | A: View Components / partials |
|---|---|---|
| Binding and validation parity | **Parity reached through the facade.** `<pems-field asp-for>` takes label, `[Required]`, kind and value from the model metadata and the first error from ModelState; invalid posts keep what the user typed; valid posts bind including an uploaded file. The spike found and fixed one facade bug (date format) that a test caught. | Native; no facade needed. |
| Antiforgery | The framework `<AntiforgeryToken />` component **works inside a statically rendered component during an MVC request** (token input present). Outside a request it throws (`HttpNavigationManager has not been initialized`), so `PemsForm` takes the request token as a parameter, which also works. A post without a token is rejected (400). | Native. |
| Render from a descriptor | **Yes, by key.** `PemsDescriptorHost` + registry render `table` from a parameter dictionary; the output equals the same table rendered from an MVC view (normalised HTML identical) and standalone with no request. | A View Component can be invoked by name, but a descriptor renderer needs a second path or every field becomes a View Component; no request-free rendering. |
| Outside a request (preview, e-mail, PDF) | **Yes** (`HtmlRenderer`, `IPemsComponentRenderer`). | No. |
| Loading from an extension assembly | **Yes.** A component in a separately built assembly, loaded at run time into its own `AssemblyLoadContext`, registered by `[PemsComponent]` and rendered by key. | Needs the assembly registered as an MVC application part; views of an extension pack need the compiled-views part. |
| RTL | Equal: the same markup and a stylesheet with logical properties only (tested: no physical left/right, no `float`, no hex colours). | Equal. |
| Offline | Equal: no URL in markup or CSS (tested); static rendering needs no circuit or WebAssembly. | Equal. |
| Testability | State is plain C#; rendering is testable with `HtmlRenderer` alone, no extra package (bUnit not needed). | Needs an MVC host or view-engine test setup. |

## Decision

1. **Canonical components are static Razor components** (no circuits, no WebAssembly), rendered through the contract in `PEMS.UI.Abstractions`.
2. **MVC pages use thin Tag Helper facades** (`<pems-field asp-for>`, later `<pems-table>`), so existing views migrate with a one-line change and keep model binding, validation and antiforgery.
3. **Performance rule, now measured:** a table renders its rows in one component; never one component per cell.
4. **Component shape:** inputs (immutable options or descriptor) / state (plain C#) / body (markup only). New components follow it.
5. **Fallback per component class (kept, not needed so far):** if a component class later fails a criterion (for example a heavy interactive grid), implement that class as a View Component behind the same descriptor contract; the contract is technology-neutral so the choice stays reversible.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| A. Tag Helpers + View Components + partials for everything | Measured equal speed, but no request-free rendering, no clean descriptor path, extension packs need MVC application parts; kept as the fallback in point 5. |
| C. Blazor interactive or an SPA | Rejected in ADR-0002 (rewrite, circuits or large downloads, offline and RTL cost); not re-opened by the spike. |

## Consequences

- Good: one rendering path for hand-built pages, the BPMS designers and out-of-request output; components are plain .NET types in assemblies.
- Cost: the facade is code to maintain for every component that pages use as an element; the framework `<AntiforgeryToken />` only works inside a request, so `PemsForm` keeps the token parameter.
- Not covered by the spike: interactive islands (JS behaviour), the data grid (sort, filter, column picker), Jalali dates, localisation keys, and a real browser check at 375 px in `fa`/`en`. Those belong to #322-#327 and the pilot wave.
- Follow-up after approval: #320 (SCRUM-29: reference `PEMS.UI` from `PEMS.Web`, move the shared partials and tokens), #321 (SCRUM-336: harden the contract), #322-#327. Update row #5 and ADR-0002 D3 to point here.
