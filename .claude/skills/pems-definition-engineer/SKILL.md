---
name: pems-definition-engineer
description: How to add or change a PEMS page, form, action, data source, menu or permission as DEFINITIONS (JSON in the data layer) with no code: file layout, operation.invoke settings (args sources, guard, sectionGuard, options objects), redirects, messages, tests and migrator feed. Use for any "convert controller X" issue and for any new screen.
---

# PEMS definition engineer

Files: `PEMS.Persistence/Definitions/System/{action,form,page,data-source,layout}/<app>/<key>.json` (fed to the database by the migrator; hot reload in dev). Hosts: `/a/{key}` action, `/f/{key}` form, `/p/{key}` page, `/d/{key}` data.

**Action** = `{kind:"action", key, appKey, version, title, payload:{kind:"handler", handler:"operation.invoke", parameters:{model, action, args, guard?, sectionGuard?}, success:{en,fa}, redirect:"/X/Details/{id}"}}`.
- `model` + `action` select a service method marked `[PemsOperation(Model, Action, Permission?)]` on its interface; the permission is a floor a definition can only add to. Run `OperationCatalog.Scan`; add a catalog permission assertion.
- `args` sources: `field:name`, `literal:text`, `context:employeeId|roles|userName|culture|ip|userAgent|userId|disciplineId`. Options objects bind from same-named fields; text lists only for parameters whose name ends in `Roles`; omitted = null; actor records are built from context by constructor parameter names.
- Answers: `(bool, string?, ...)`, bool, void, int; business exceptions (InvalidOperation, UnauthorizedAccess, DbUpdateConcurrency, Exception) become messages.
- `guard {resource, action, resourceId, mode:"sod"|"strict", message?}` = access control plus denial record; `sectionGuard {section, action, workOrderId, reason, recordRef, audit, recordRefFromResult, beforeFromResult}` = Work Order section x phase x permission plus change log.
- Redirect placeholders `{name}` come from posted values; a new id from a result needs the result-binding step (SCRUM-594).

**Procedure for a controller issue**: inventory its actions (`plan/sheets/definition-inventory.md`); if it uses `PEMSDbContext` inline, extract a service first (Model + Action, keep every message and rule); write the definitions; point the view forms at `/a/<key>`; delete the controller action, view and inline script; test through the real handler; add the feed; list browser QA lines. Never add a handler per entity: if the generic one cannot express it, the issue is an engine issue (guideline 04 section 7), not a special case.

UI uses `pems-*` components only (guideline 02); text is `TranslatableText` fa and en.
