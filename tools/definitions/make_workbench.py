#!/usr/bin/env python3
"""Writes the definition files of the definition workbench (SCRUM-449, 450, 451, 455, 454, 439, 604, 606, 607): the workbench is made of definitions over the definition store, not of compiled screens.

    python3 tools/definitions/make_workbench.py

data-source: definitions-grid, definition-kinds-grid, definition-usages-grid  (providers in PEMS.Web/Definitions/DefinitionWorkbenchProviders.cs, permission Definitions.View)
action:      definition-create, definition-save-draft, definition-validate, definition-publish, definition-discard, definition-rollback  (operation.invoke over IDefinitionWorkbench)
form:        definition-create, definition-save-draft
page:        definition-workbench (list), definition-details (record + usages), definition-kinds (kind catalog and storage report)
"""
import json, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "PEMS.Persistence", "Definitions", "System")


def t(en, fa):
    return {"en": en, "fa": fa}


def write(kind, key, title, desc, payload):
    path = os.path.join(OUT, kind, "core", key + ".json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump({"kind": kind, "key": key, "appKey": "core", "version": 1, "title": title, "description": desc, "payload": payload}, f, ensure_ascii=False, indent=2)


def source(key, provider, title_en, title_fa):
    write("data-source", key, t(title_en, title_fa), t(title_en, title_fa), {"kind": "grid", "provider": provider, "permission": "Definitions.View", "parameters": {}})


source("definitions-grid", "definitions", "Definitions", "تعریف‌ها")
source("definition-kinds-grid", "definition-kinds", "Definition kinds", "انواع تعریف")
source("definition-usages-grid", "definition-usages", "Where a definition is used", "محل استفاده از تعریف")
source("definition-largest-grid", "definition-largest", "Largest definitions", "بزرگ‌ترین تعریف‌ها")
source("definition-translations-grid", "definition-translations", "Missing translations", "ترجمه‌های ناقص")


def action(key, title, args, ok, redirect="/p/definition-workbench"):
    write("action", key, title, title, {"kind": "handler", "handler": "operation.invoke", "success": ok, "redirect": redirect,
                                        "parameters": {"model": "Definition", "action": args.pop("_action"), "args": args}})


actor = "context:userName"
action("definition-create", t("Create definition", "ایجاد تعریف"),
       {"_action": "Create", "kind": "field:kind", "key": "field:key", "appKey": "field:appKey", "titleEn": "field:titleEn", "titleFa": "field:titleFa", "payloadJson": "field:payload", "changeSummary": "field:changeSummary", "actor": actor},
       t("The definition was created as a draft-free first version; publish it to use it.", "تعریف ایجاد شد؛ برای استفاده منتشرش کنید."))
action("definition-save-draft", t("Save draft", "ذخیره پیش‌نویس"),
       {"_action": "SaveDraft", "kind": "field:kind", "key": "field:key", "appKey": "field:appKey", "payloadJson": "field:payload", "changeSummary": "field:changeSummary", "actor": actor},
       t("The draft was saved.", "پیش‌نویس ذخیره شد."))
for k, op, en, fa, ok_en, ok_fa in (("validate", "Validate", "Validate draft", "اعتبارسنجی پیش‌نویس", "The draft is valid.", "پیش‌نویس معتبر است."),
                                    ("publish", "Publish", "Publish draft", "انتشار پیش‌نویس", "The draft was published.", "پیش‌نویس منتشر شد."),
                                    ("discard", "DiscardDraft", "Discard draft", "حذف پیش‌نویس", "The draft was discarded.", "پیش‌نویس حذف شد.")):
    action("definition-" + k, t(en, fa), {"_action": op, "reference": "field:id", "actor": actor}, t(ok_en, ok_fa))
action("definition-rollback", t("Roll back to a version", "بازگشت به نسخه"),
       {"_action": "Rollback", "reference": "field:id", "toVersion": "field:toVersion", "actor": actor}, t("The definition was rolled back.", "تعریف به نسخه قبلی برگشت."))


def form(key, title_en, title_fa, submit_en, submit_fa, permission, fields):
    write("form", key, t(title_en, title_fa), t(title_en, title_fa), {"id": key, "title": t(title_en, title_fa), "submitLabel": t(submit_en, submit_fa), "actionKey": key, "permission": permission,
                                                                         "columns": 2, "warnOnLeave": True, "fields": fields})


def field(name, en, fa, kind="text", required=False, **extra):
    f = {"name": name, "label": t(en, fa), "kind": kind}
    if required:
        f["required"] = True
        f["messages"] = {"required": t(en + " is required.", fa + " الزامی است.")}
    f.update(extra)
    return f


payload_help = t("The JSON payload of the definition, checked by the kind when you save.", "بار JSON تعریف؛ هنگام ذخیره توسط نوع آن بررسی می‌شود.")
form("definition-create", "New definition", "تعریف جدید", "Create", "ایجاد", "Definitions.Create", [
    field("kind", "Kind", "نوع", required=True, maxLength=60, placeholder=t("page, form, action, data-source, component, route, layout, menu", "page, form, action, ...")),
    field("key", "Key", "کلید", required=True, maxLength=200, pattern="[a-z0-9][a-z0-9._-]*", messages={"pattern": t("Lower-case letters, digits and . _ - only.", "فقط حروف کوچک، عدد و . _ -")}),
    field("appKey", "App", "برنامه", maxLength=100, placeholder=t("core", "core")),
    field("titleEn", "Title (English)", "عنوان (انگلیسی)", required=True, maxLength=200),
    field("titleFa", "Title (Persian)", "عنوان (فارسی)", required=True, maxLength=200),
    field("payload", "Payload (JSON)", "بار (JSON)", "textArea", required=True, maxLength=1000000, span=2, helpText=payload_help),
    field("changeSummary", "What this version is", "این نسخه چیست", maxLength=500, span=2)])
form("definition-save-draft", "Save a draft", "ذخیره پیش‌نویس", "Save draft", "ذخیره", "Definitions.Edit", [
    field("kind", "Kind", "نوع", required=True, maxLength=60),
    field("key", "Key", "کلید", required=True, maxLength=200),
    field("appKey", "App", "برنامه", maxLength=100, placeholder=t("core", "core")),
    field("payload", "Payload (JSON)", "بار (JSON)", "textArea", required=True, maxLength=1000000, span=2, helpText=payload_help),
    field("changeSummary", "What changed", "چه تغییر کرد", maxLength=500, span=2)])

# ---- pages
cols = [{"key": "key", "header": t("Key", "کلید"), "width": 240}, {"key": "kind", "header": t("Kind", "نوع"), "width": 130}, {"key": "appKey", "header": t("App", "برنامه"), "width": 90},
        {"key": "title", "header": t("Title", "عنوان"), "width": 220}, {"key": "origin", "header": t("Origin", "منشأ"), "width": 100},
        {"key": "publishedVersion", "header": t("Published", "منتشرشده"), "width": 100, "type": "number"}, {"key": "hasDraft", "header": t("Draft", "پیش‌نویس"), "width": 90, "type": "bool"},
        {"key": "isCustomized", "header": t("Customised", "سفارشی"), "width": 100, "type": "bool"},
        {"key": "_actions", "header": t("Actions", "عملیات"), "type": "actions", "width": 420, "sortable": False, "filterable": False, "actions": [
            {"key": "details", "label": t("Details", "جزئیات"), "url": "/p/definition-details?id={id}", "method": "get"},
            {"key": "validate", "label": t("Validate", "اعتبارسنجی"), "url": "/a/definition-validate", "method": "post"},
            {"key": "publish", "label": t("Publish", "انتشار"), "url": "/a/definition-publish", "method": "post", "tone": "primary", "confirm": t("Publish the draft?", "پیش‌نویس منتشر شود؟")},
            {"key": "discard", "label": t("Discard draft", "حذف پیش‌نویس"), "url": "/a/definition-discard", "method": "post", "tone": "danger", "confirm": t("Discard the draft?", "پیش‌نویس حذف شود؟")}]}]
write("page", "definition-workbench", t("Definition workbench", "میز کار تعریف‌ها"), t("Every definition of the store: create, edit as a draft, validate, publish, roll back", "همه تعریف‌ها"),
      {"layout": "list-page", "route": "definition-workbench", "permission": "Definitions.View", "template": "entity-list",
       "args": {"id": "definition-workbench", "url": "/d/definitions-grid", "idField": "ref", "viewsKey": "definition-workbench", "newUrl": "/f/definition-create", "newLabel": t("New definition", "تعریف جدید"), "columns": cols}})

fields = [{"key": "key", "label": t("Key", "کلید"), "mono": True}, {"key": "kind", "label": t("Kind", "نوع"), "mono": True}, {"key": "appKey", "label": t("App", "برنامه"), "mono": True},
          {"key": "title", "label": t("Title", "عنوان")}, {"key": "origin", "label": t("Origin", "منشأ")}, {"key": "publishedVersion", "label": t("Published version", "نسخه منتشرشده")},
          {"key": "hasDraft", "label": t("Has a draft", "پیش‌نویس دارد"), "type": "bool"}, {"key": "isCustomized", "label": t("Customised", "سفارشی"), "type": "bool"},
          {"key": "revision", "label": t("Revision", "بازنگری")}, {"key": "createdAt", "label": t("Created", "ایجاد"), "type": "datetime"}]
usage_cols = [{"key": "fromKind", "header": t("Kind", "نوع"), "width": 140}, {"key": "fromKey", "header": t("Key", "کلید"), "width": 260}, {"key": "role", "header": t("Role", "نقش"), "width": 140}]
write("page", "definition-details", t("Definition details", "جزئیات تعریف"), t("One definition and where it is used", "یک تعریف و محل استفاده"),
      {"layout": "detail-page", "route": "definition-details", "permission": "Definitions.View", "template": "entity-details",
       "args": {"id": "definition-record", "url": "/d/definitions-grid", "idField": "ref", "fields": fields, "backUrl": "/p/definition-workbench", "backLabel": t("Back to the workbench", "بازگشت به میز کار")},
       "regions": {"content": [{"id": "usages", "component": "data-grid", "options": {"id": "definition-usages", "url": "/d/definition-usages-grid?f.ref=eq:{{params.id|url}}", "idField": "ref", "columns": usage_cols, "selectable": False,
                                                                                    "texts": {"search": t("Search", "جستجو"), "columns": t("Columns", "ستون‌ها"), "density": t("Density", "تراکم"), "compact": t("Compact", "فشرده"), "comfortable": t("Comfortable", "راحت"),
                                                                                              "export": t("Export", "خروجی"), "views": t("Views", "نماها"), "saveView": t("Save view", "ذخیره نما"), "viewName": t("View name", "نام نما"),
                                                                                              "loading": t("Loading", "در حال بارگذاری"), "empty": t("Not used by another definition", "تعریف دیگری از آن استفاده نمی‌کند"),
                                                                                              "error": t("Could not load", "بارگذاری نشد"), "rows": t("{0} rows", "{0} ردیف"), "selected": t("{0} selected", "{0} انتخاب شد"),
                                                                                              "selectAllMatching": t("Select all {0}", "انتخاب همه {0}"), "clearSelection": t("Clear", "پاک کردن"), "filter": t("Filter", "فیلتر"), "delete": t("Delete", "حذف")}}}]}})

kind_cols = [{"key": "kind", "header": t("Kind", "نوع"), "width": 160}, {"key": "owner", "header": t("Owner", "مالک"), "width": 120}, {"key": "definitions", "header": t("Definitions", "تعریف‌ها"), "width": 120, "type": "number"},
             {"key": "published", "header": t("Published", "منتشرشده"), "width": 120, "type": "number"}, {"key": "drafts", "header": t("Drafts", "پیش‌نویس‌ها"), "width": 110, "type": "number"},
             {"key": "versions", "header": t("Versions", "نسخه‌ها"), "width": 110, "type": "number"}, {"key": "payloadBytes", "header": t("Published bytes", "حجم منتشرشده"), "width": 150, "type": "number"}]
write("page", "definition-kinds", t("Definition kinds and storage", "انواع تعریف و حجم ذخیره"), t("Every registered kind with its owner, counts and published size", "هر نوع ثبت‌شده با مالک، شمار و حجم"),
      {"layout": "list-page", "route": "definition-kinds", "permission": "Definitions.View", "template": "entity-list",
       "args": {"id": "definition-kinds", "url": "/d/definition-kinds-grid", "idField": "kind", "columns": kind_cols},
       "regions": {"kpis": [{"id": "size", "component": "def:entity-chart", "options": {"url": "/d/definition-kinds-grid", "labelField": "kind", "series": [{"field": "payloadBytes", "name": t("Published bytes", "حجم منتشرشده")}],
                                                                                   "kind": "hbar", "sort": "-payloadBytes", "ariaLabel": t("Published definition size by kind", "حجم تعریف‌ها بر اساس نوع")}}]}})
tr_cols = [{"key": "kind", "header": t("Kind", "نوع"), "width": 110}, {"key": "key", "header": t("Key", "کلید"), "width": 240}, {"key": "path", "header": t("Where", "محل"), "width": 320},
           {"key": "missing", "header": t("Missing language", "زبان ناقص"), "width": 130}, {"key": "shown", "header": t("Shown instead", "متن جایگزین"), "width": 260}]
write("page", "definition-translations", t("Missing translations", "ترجمه‌های ناقص"), t("Every text of a published form, page or menu that has no text in one language, with what a user sees instead", "هر متنی که یک زبان ندارد"),
      {"layout": "list-page", "route": "definition-translations", "permission": "Definitions.View", "template": "entity-list",
       "args": {"id": "definition-translations", "url": "/d/definition-translations-grid", "idField": "ref", "columns": tr_cols}})
big_cols = [{"key": "kind", "header": t("Kind", "نوع"), "width": 110}, {"key": "key", "header": t("Key", "کلید"), "width": 260}, {"key": "versions", "header": t("Versions", "نسخه‌ها"), "width": 110, "type": "number"},
            {"key": "totalBytes", "header": t("All versions, bytes", "حجم همه نسخه‌ها"), "width": 170, "type": "number"}]
write("page", "definition-storage", t("Definition storage", "حجم ذخیره تعریف‌ها"), t("The definitions that take the most space; versions are kept forever", "تعریف‌های پرحجم؛ نسخه‌ها همیشه نگهداری می‌شوند"),
      {"layout": "list-page", "route": "definition-storage", "permission": "Definitions.View", "template": "entity-list",
       "args": {"id": "definition-storage", "url": "/d/definition-largest-grid", "idField": "ref", "columns": big_cols}})
print("ok")
