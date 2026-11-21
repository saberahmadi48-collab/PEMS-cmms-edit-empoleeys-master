# Overview

PEMS - Platform Overview

Read this page first, before Read Me / Instructions / Backlog, in any new session. It is the fastest orientation to what PEMS is, where it is going and how work is done here. Update it whenever something it describes goes stale (not only Instructions/Memory).

What PEMS is

PEMS is an enterprise .NET system (ASP.NET Core MVC/Razor + EF Core 10 + SQL Server) for engineering and maintenance management in the oil and petrochemical industry: EDMS/VDR, CMMS/EAM, permit to work (PTW), inventory, production, reliability and local AI (Ollama/OCR). Today it is a set of fixed modules (98 controllers, 91 View folders, 116 migrations, 1,403 tests). PEMS is a brand-new product whose long-term aim is to become the single platform for its customers' engineering and maintenance processes.

New direction: from fixed software to a dynamic BPMS

Forms, modules and workflows must be fully creatable and editable by authorized users, with no coding and no build/deploy. Today's fixed modules are progressively converted into metadata definitions (strangler pattern, backlog area MIG) and specialized parts (AI, OCR, EDMS revision engine, reliability) stay as Domain Packs with extension points. Shared engines: metadata registry, form, workflow, rules, access/RLS, events, reporting.

Commercial model

For now, an on-prem licence locked to the machine and the install time: even if a whole machine is imaged and deployed elsewhere it must not work. Process: a request file from the customer server, returned to the vendor, a signed licence file issued, installed. Licence types: lifetime, term (a time period), trial, demo. Details in the Licensing Design sheet and the LIC area. The product never calls out (production has no internet); the website and the demo are the only online parts.

Prioritization rule of this backlog

Priority is set by 'ability to sell' and 'dynamic core': P0 = without it nothing can be sold or the dynamic platform cannot form (foundation decisions, metadata, grid/form, licensing, RLS, demo, install); P1 = needed for commercial release 1.0; P2 = competitive/next stage; P3 = later. The legacy domain backlog (CMMS/EDMS etc.) matters but stays behind sales and dynamicization work unless it blocks them.

Current state (estimated from code, no build executed)

Strengths: domain breadth, modern authentication (Identity + FIDO2), a central access model, a workflow runtime, local AI. Main gaps: no App/Entity/Form registry and no form builder; no workflow studio; no licensing and no installer; no server-side session registry; no Jalali UI; default culture is en. No build/test has been run (row F_BASE). Database creation, install and update are fully code-first (migrator + Admin Database Update page, 2026-10-22).

Development strategy

Plan-file-driven: the plan folder (plan/rows and plan/sheets) is the single source of truth for task status and is updated within each task. First scope, then update the plan, then design with a library-first, best-practice approach, then implement. Go forward, not backward-compatible; applied migrations are never edited. Marketing claims cover only what is Done and tested in the Backlog.

Where to go next

'Is X done / status of Y?' -> Backlog (Evidence column). 'What is next?' -> Roadmap. 'Which rules always apply?' -> Instructions. 'What do we know from earlier platforms?' -> Lessons. 'Which capabilities must PEMS offer?' -> Capability Checklist. 'How does licensing work?' -> Licensing Design. 'What is supported and up to which limits?' -> Supportability Matrix. 'What happened to an old task?' -> Old to New Map.
