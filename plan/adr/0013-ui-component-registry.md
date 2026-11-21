# ADR-0013: UI component registry: code reaches components only through registry keys

- **Status:** Accepted
- **Date:** 2026-11-05
- **Decided by:** Hamid Maleki (owner): asked for "a ui library dispatcher-like, linked to components, and the code links to this dispatcher" and approved the proposal ("Plan your proposal and do it") on 2026-11-05
- **Plan rows:** #506 (SCRUM-506); builds on ADR-0007, ADR-0011 (#430); used by #387, #438
- **Sources:** owner messages 2026-11-05; audit of `PEMS.UI` the same day (below)

## Context

- `IPemsComponentRegistry` maps 26 keys to component types and `IPemsComponentRenderer` renders a key with no HTTP request. The dynamic path (`PemsDescriptorHost`, descriptors, the future page host #438) already uses it.
- The static path does not: of 60 `pems-*` tag helpers, 13 render through the registry, 2 are child parts and 47 write their own markup. Keys were string literals inside the registry. Views link to a tag helper and the graph never shows which component it stands for, so "who uses `badge`" cannot be answered, and nothing stops a new tag from bypassing the registry.
- ADR-0011 says dynamic pages render through the same components as hand-built ones. That is only true if both paths enter through the same door.

## Decision

We keep one door: **the registry key**. Views and descriptors both name a component by its key; the registry resolves it; the renderer renders it.

1. `UiKeys` holds the keys as constants. Code never writes a key as a string literal; the registry registers with the constants.
2. Every `pems-*` tag helper carries `[PemsFacade(...)]`: `UiKeys.X` (it renders that registered component), `FacadeKind.Part` (a child tag of another facade) or `FacadeKind.Direct` (it still writes its own markup: a debt).
3. Drift tests fail when a tag helper has no declaration, when a declared key is not registered or the helper does not render that component, when `UiKeys` and the registry differ, and when the number of Direct facades rises above the baseline. The baseline only goes down.
4. A new component is added in one step: component, `[PemsComponent]`/registry row with a `UiKeys` constant, facade (or descriptor-only), guideline 02 row, test.
5. The code graph gets a node per key with edges key -> component, key -> facade and view -> key (built by `tools/graphify/update.py`), so the graph shows the registry.
6. Direct facades move to registered components in later slices, family by family, each with default output unchanged (golden manifests and the adoption ratchet stay green).

## Naming

The owner asked for a "dispatcher-like" library and left the name open. The established name for a map from a key or type name to a UI component, used to render data-driven UI, is **component registry** (Apollo GraphOS server-driven UI guide: "this mapping is called a component registry"; the keyed-registry-with-fallback pattern in React server-driven UI articles). "Dispatcher" and "mediator" name different patterns (routing a message or a command to a handler). The code already says registry (`IPemsComponentRegistry`), so we keep that name and add no second term. Practices taken from the same sources: a keyed map, never a switch; an unknown key fails loudly (`Resolve` throws), never renders nothing; looks (colours) come from design tokens, not from the descriptor.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Leave tag helpers as they are and only document the registry | The gap grows with every new tag; the graph stays blind to components. |
| Rewrite all 47 Direct tag helpers now | A very large change with output risk in 340 views; done in slices instead, behind the baseline. |
| A separate "dispatcher" or mediator object between views and the registry | The registry plus the renderer already do the work; a third layer adds indirection and no capability. |
| Source generators for keys and facades | More tooling than 26 constants need now; reconsider when extension packs add keys. |

## Consequences

- Good: one vocabulary for static and dynamic pages; drift is caught by tests, not by review; the graph answers "which pages use component X".
- Cost: an attribute on 60 classes and one baseline to maintain; the 47 Direct facades remain until converted.
- Follow-up work: convert Direct facades by family (own plan rows), page host (#438) through the registry only.

## Amendments

- 2026-11-05 (owner): no exempt facade kind. Form-control tag helpers and button also render registered components; `DirectFacadeBaseline` goes to 0; each batch creates its components first, then converts the code. Guideline 02 section 7 items 2, 4, 7.
- 2026-11-06: SCRUM-507 finished. `FacadeKind.Direct` is removed from the code (60 of 60 tag helpers render a registered component or are a part of one); the drift test pins the kinds to Component and Part.
