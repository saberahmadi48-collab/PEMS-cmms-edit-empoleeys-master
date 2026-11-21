#!/usr/bin/env python3
"""One-off (2026-11-20): plan rows of the gap issues SCRUM-784..801 (guideline 07 section 1c). Run from the repository root."""
import subprocess, sys

META = "Metadata Core & Dynamic Platform"
COMP = "Component Library & Design System"
INST = "Installation, Deployment & Updates"
ROWS = [
    (784, "Definition manager version history, payload diff, export and import screens (gap of SCRUM-449)", META, "L"),
    (785, "Used-by and references REST endpoints and publish-time reference check in the REST publish (gap of SCRUM-450)", META, "M"),
    (786, "Storage growth per month, cache statistics, RetainRetiredVersions setting and size warning (gap of SCRUM-455)", META, "S"),
    (787, "Fallback preview and live fa/en, RTL/LTR preview of a definition (gap of SCRUM-419 and 609)", META, "M"),
    (788, "Smoke test with sample data, post-publish smoke check with automatic rollback, acknowledgement of breaking changes (gap of SCRUM-457)", META, "L"),
    (789, "Precheck-rollback REST endpoint, verdict per retired version on the screen, auto-rollback loop guard (gap of SCRUM-459)", META, "S"),
    (790, "Browser tests of the definition API client against a stub server and its catalog documentation (gap of SCRUM-454)", META, "S"),
    (791, "Test that every migration and shipped definition file is covered by the change manifest (gap of SCRUM-585)", INST, "S"),
    (792, "Live preview of each component sample in fa and en with RTL/LTR and theme toggles in the catalog (gap of SCRUM-340)", COMP, "M"),
    (793, "Saved views per role stored on the server for generic list pages (gap of SCRUM-599)", COMP, "M"),
    (794, "Filter bar as a part of the list template (gap of SCRUM-599)", COMP, "S"),
    (795, "Related-records part (child tables filtered by the record id) on the details template (gap of SCRUM-600)", COMP, "S"),
    (796, "Mark the platform fields of the shipped forms, owner decides which fields are ours (gap of SCRUM-515)", META, "S"),
    (797, "Show the form-ownership preflight report on Admin Database Update and /health/database (gap of SCRUM-515)", META, "S"),
    (798, "ADR for field ownership, platform and application fields (gap of SCRUM-515)", META, "S"),
    (799, "Prove action.steps with a shipped multi-table action definition (gap of SCRUM-593)", META, "S"),
    (800, "Generic PDF output for file results, QuestPDF documents from definitions (gap of SCRUM-615)", META, "M"),
    (801, "Download of a stored file by key with a record-level authorization decision (gap of SCRUM-615)", META, "M"),
]
for n, title, area, effort in ROWS:
    r = subprocess.run([sys.executable, "tools/plan-sync/plan_sync.py", "new", str(n), title, f"area={area}", "group=Definition engines", "doer=Claude", "milestone=M2", f"effort={effort}", "priority=P1", "status=Not Started"], capture_output=True, text=True)
    print(n, r.returncode, (r.stdout + r.stderr).strip()[:120])
