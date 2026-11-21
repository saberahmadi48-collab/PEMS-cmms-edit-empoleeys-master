#!/usr/bin/env python3
"""Definition files of the migrator center (SCRUM-582): the action and the form that upload a signed package and show what the migrator would do (operation Migrator.Inspect)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_workbench import t, write, form, field  # noqa: E402

write("action", "migrator-inspect", t("Inspect a migration package", "بررسی بسته مهاجرت"), t("Verifies the signature and shows what the migrator would do", "امضا را بررسی و کار مهاجرت‌گر را نشان می‌دهد"),
      {"kind": "handler", "handler": "operation.invoke", "success": t("The package was read.", "بسته خوانده شد."), "redirect": "/f/migrator-inspect",
       "parameters": {"model": "Migrator", "action": "Inspect", "args": {"package": "file:file", "actor": "context:userName"}, "upload": {"profile": "migrationPackage", "maxFileMb": 100}}})
form("migrator-inspect", "Migrator center: inspect a package", "مرکز مهاجرت: بررسی بسته", "Inspect", "بررسی", "Definitions.Create", [
    {"name": "file", "label": t("Package (.zip)", "بسته (‎.zip)"), "kind": "file", "required": True}])
# the form posts to its action
import json
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "PEMS.Persistence", "Definitions", "System", "form", "core", "migrator-inspect.json")
with open(path, encoding="utf-8") as f:
    d = json.load(f)
d["payload"]["actionKey"] = "migrator-inspect"
with open(path, "w", encoding="utf-8", newline="") as f:
    json.dump(d, f, ensure_ascii=False, indent=2)
print("migrator ok")
