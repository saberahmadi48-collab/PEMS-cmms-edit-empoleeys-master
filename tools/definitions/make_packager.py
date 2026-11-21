#!/usr/bin/env python3
"""Definition files of the vendor package panel (SCRUM-584): forms and actions that build, sign and download an install, update or feed package (operations Packager.*). Works only on the vendor deployment that holds the signing key."""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_workbench import t, write, form, field, OUT  # noqa: E402


def packager_action(key, op, title_en, title_fa, args):
    write("action", key, t(title_en, title_fa), t(title_en, title_fa),
          {"kind": "handler", "handler": "operation.invoke", "parameters": {"model": "Packager", "action": op, "args": args}})


packager_action("packager-feed", "BuildFeed", "Build a feed package", "ساخت بسته داده", {"toVersion": "field:toVersion", "entity": "field:entity", "mode": "field:mode", "rowsJson": "field:rows"})
packager_action("packager-install", "BuildInstall", "Build an install package", "ساخت بسته نصب", {"toVersion": "field:toVersion", "schemaLevel": "field:schemaLevel", "definitionRefs": "field:definitions"})
packager_action("packager-update", "BuildUpdate", "Build an update package", "ساخت بسته به‌روزرسانی", {"fromVersion": "field:fromVersion", "toVersion": "field:toVersion", "schemaLevel": "field:schemaLevel", "definitionRefs": "field:definitions"})

defs = field("definitions", "Definitions (appKey:kind:key, comma separated)", "تعریف‌ها (appKey:kind:key)", "textArea", maxLength=20000, span=2)
form("packager-feed", "Vendor: build a feed package", "فروشنده: ساخت بسته داده", "Build and download", "ساخت و دریافت", "Definitions.Create", [
    field("toVersion", "Product version", "نسخه محصول", required=True, maxLength=30), field("entity", "Entity", "موجودیت", required=True, maxLength=100),
    field("mode", "Mode (append, overwrite or merge)", "حالت (append، overwrite یا merge)", required=True, maxLength=20),
    field("rows", "Rows (JSON array of objects)", "ردیف‌ها (آرایه JSON)", "textArea", required=True, maxLength=2000000, span=2)])
form("packager-install", "Vendor: build an install package", "فروشنده: ساخت بسته نصب", "Build and download", "ساخت و دریافت", "Definitions.Create", [
    field("toVersion", "Product version", "نسخه محصول", required=True, maxLength=30), field("schemaLevel", "Schema level (last migration id)", "سطح شما", required=True, maxLength=200), defs])
form("packager-update", "Vendor: build an update package", "فروشنده: ساخت بسته به‌روزرسانی", "Build and download", "ساخت و دریافت", "Definitions.Create", [
    field("fromVersion", "From version", "از نسخه", required=True, maxLength=30), field("toVersion", "To version", "به نسخه", required=True, maxLength=30),
    field("schemaLevel", "Schema level (last migration id)", "سطح شما", required=True, maxLength=200), defs])
for k in ("packager-feed", "packager-install", "packager-update"):
    path = os.path.join(OUT, "form", "core", k + ".json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    d["payload"]["actionKey"] = k
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
print("packager ok")
