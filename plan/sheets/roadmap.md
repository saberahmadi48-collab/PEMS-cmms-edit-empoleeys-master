# Roadmap

| Epic / Milestone | When | Priority | Areas Involved | Rationale | Tasks |
| --- | --- | --- | --- | --- | --- |
| M0 - Baseline and foundation decisions | Now | P0 | FND, LIC, BRD, DEM, WEB, MIG, CMP, QA | Without the decisions on storage, UI technology, libraries, the licensing threat model and editions nothing later is stable. Decisions are cheap and unblock everything. In parallel run a real build/test and clean secrets from the repo. | auto |
| M1 - Dynamic core, components and licensing (Alpha) | Now | P0 | DYN, CMP, LIC, SEC, LOC | These block everything else: metadata registry, dynamic API with RLS, safe schema change, grid/form, Persian/Jalali, and the whole licence chain (fingerprint, install seal, request file, signed licence, vendor tool, anti-clone). Selling without a licence and without dynamism is meaningless. | auto |
| M2 - Builders, demo and install (Beta) | Next | P0 | FRM, MOD, WFL, MIG, DEM, INS, BRD | The form, module and workflow builders plus the first migration wave (wave 1) prove the engine works. The demo and the installer with signed updates allow sales to begin. | auto |
| M3 - Commercial release 1.0: demo, website, brand and legal | Next | P1 | LIC, BRD, DEM, WEB, INS, DEV, CVM, HST | Sellable packaging: licence activation portal, website, sales material, pilot customer, licence upgrade/transfer/renewal and support, plus the customer registry and release process needed before the first customer is live. | auto |
| M4 - Platform completion and module migration | Next | P1 | DYN, CMP, FRM, MOD, WFL, MIG, SEC, INT, QA, LOC, RPT, AI, OPS, EDM, EAM | Release 1.0 must be able to host every wave: the rest of the platform P1 work, wave 2, mobile, SSO, security and the report builder. | auto |
| M5 - Competitive features (P2) | Later | P2 | All | Capabilities that make selling easier but do not block 1.0. | auto |
| M6 - Later (P3) | Later | P3 | All | Highest uncertainty and lowest sales impact. | auto |

Suggested order for the first 60 days (after owner approval)

Week 1: F_BASE (real build/test), F_SECRET (repo secrets), F_ADR, and the F_STORE / F_UI / F_LIBS decisions with small prototypes.

Weeks 1-2: owner decisions: VM/container policy (L_TIMES), edition matrix (L_EDITION), pricing (B_PRICE), legal structure (B_ENTITY).

Weeks 2-4: L_MODEL (threat model) and L_FP + L_INSTALLID (proof: an image on other hardware does not run); C_AUDIT; M_AUDIT.

Weeks 3-8: vertical slice: D_OBJ + D_API + C_GRID + FRM_RUN on one sample entity (migration wave 1 pilot); S_RLS in parallel.

Weeks 5-8: L_REQ -> L_LICFMT -> L_VENDOR -> L_UI -> L_FIRSTRUN (the complete licence chain) and LOC_DEFAULT / LOC_CAL.

End of 60 days: an internal demo of 'build a module in the UI and run it' with the machine-locked licence working; then M2.
