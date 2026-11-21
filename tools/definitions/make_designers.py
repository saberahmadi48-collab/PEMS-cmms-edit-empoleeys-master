#!/usr/bin/env python3
"""Definition files of the designers (SCRUM-439 page, 604 form, 606 action, 607 data source, 608 menu): stored data sources, actions, forms and pages over the Designer operations (IDefinitionDesigner).

    python3 tools/definitions/make_designers.py        (run from the repository root; it also regenerates the workbench files)
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_workbench import t, write, source, form, field, OUT  # noqa: E402  (importing it writes the workbench files)

source("definition-payload-grid", "definition-payload", "Parts of a definition", "اجزای تعریف")
source("operation-catalog-grid", "operation-catalog", "Operations", "عملیات‌ها")


def designer_action(key, op, title_en, title_fa, args, ok_en, ok_fa):
    args = dict(args)
    args["actor"] = "context:userName"
    write("action", key, t(title_en, title_fa), t(title_en, title_fa), {"kind": "handler", "handler": "operation.invoke", "success": t(ok_en, ok_fa), "redirect": "/p/definition-workbench",
                                                                          "parameters": {"model": "Designer", "action": op, "args": args}})


designer_action("designer-remove-row", "RemoveRow", "Remove a part", "حذف بخش", {"rowId": "field:id"}, "The part was removed from the draft.", "بخش از پیش‌نویس حذف شد.")
designer_action("designer-move-up", "MoveRow", "Move up", "انتقال به بالا", {"rowId": "field:id", "direction": "literal:up"}, "The part moved up in the draft.", "بخش در پیش‌نویس بالا رفت.")
designer_action("designer-move-down", "MoveRow", "Move down", "انتقال به پایین", {"rowId": "field:id", "direction": "literal:down"}, "The part moved down in the draft.", "بخش در پیش‌نویس پایین رفت.")
designer_action("designer-add-form-field", "AddFormField", "Add a form field", "افزودن فیلد فرم",
                {"reference": "field:ref", "name": "field:name", "kind": "field:kind", "titleEn": "field:titleEn", "titleFa": "field:titleFa", "required": "field:required"}, "The field was added to the draft.", "فیلد به پیش‌نویس افزوده شد.")
designer_action("designer-add-page-instance", "AddPageInstance", "Add a component to a page", "افزودن مؤلفه به صفحه",
                {"reference": "field:ref", "region": "field:region", "id": "field:id", "component": "field:component", "optionsJson": "field:options"}, "The component was added to the draft.", "مؤلفه به پیش‌نویس افزوده شد.")
designer_action("designer-add-menu-item", "AddMenuItem", "Add a menu item", "افزودن آیتم منو",
                {"reference": "field:ref", "itemKey": "field:itemKey", "parent": "field:parent", "titleEn": "field:titleEn", "titleFa": "field:titleFa", "page": "field:page", "order": "field:order"}, "The menu item was added to the draft.", "آیتم منو به پیش‌نویس افزوده شد.")
designer_action("designer-set-value", "SetValue", "Set a value of the draft", "تنظیم مقدار پیش‌نویس", {"reference": "field:ref", "pointer": "field:pointer", "valueJson": "field:value"}, "The value was set in the draft.", "مقدار در پیش‌نویس تنظیم شد.")
designer_action("designer-create-action", "CreateAction", "Create an action", "ایجاد کنش",
                {"key": "field:key", "appKey": "field:appKey", "model": "field:model", "action": "field:action", "argsJson": "field:args", "successEn": "field:successEn", "successFa": "field:successFa", "redirect": "field:redirect"},
                "The action was created; publish it from the workbench.", "کنش ایجاد شد؛ از میز کار منتشرش کنید.")


def ref_field():
    return field("ref", "Definition", "تعریف", required=True, maxLength=300, placeholder=t("appKey:kind:key, for example core:form:department-create", "appKey:kind:key"))


form("designer-form-field", "Add a form field", "افزودن فیلد فرم", "Add", "افزودن", "Definitions.Edit", [ref_field(),
    field("name", "Name", "نام", required=True, maxLength=100), field("kind", "Type", "نوع", maxLength=40, placeholder=t("text, textArea, select, date, number, checkbox", "text")),
    field("titleEn", "Label (English)", "برچسب (انگلیسی)", required=True, maxLength=200), field("titleFa", "Label (Persian)", "برچسب (فارسی)", required=True, maxLength=200),
    field("required", "Required (true or false)", "اجباری (true یا false)", maxLength=5)])
form("designer-page-instance", "Add a component to a page", "افزودن مؤلفه به صفحه", "Add", "افزودن", "Definitions.Edit", [ref_field(),
    field("region", "Region", "ناحیه", required=True, maxLength=60), field("id", "Instance id", "شناسه نمونه", required=True, maxLength=100),
    field("component", "Component", "مؤلفه", required=True, maxLength=100, placeholder=t("a key of the component catalog, or def:<component definition>", "کلید مؤلفه")),
    field("options", "Options (JSON)", "گزینه‌ها (JSON)", "textArea", maxLength=100000, span=2)])
form("designer-menu-item", "Add a menu item", "افزودن آیتم منو", "Add", "افزودن", "Definitions.Edit", [ref_field(),
    field("itemKey", "Key", "کلید", required=True, maxLength=100), field("parent", "Parent key", "کلید والد", maxLength=100),
    field("titleEn", "Title (English)", "عنوان (انگلیسی)", required=True, maxLength=200), field("titleFa", "Title (Persian)", "عنوان (فارسی)", required=True, maxLength=200),
    field("page", "Page key", "کلید صفحه", required=True, maxLength=100), field("order", "Order", "ترتیب", maxLength=6)])
form("designer-set-value", "Set a value of a definition draft", "تنظیم مقدار در پیش‌نویس", "Set", "تنظیم", "Definitions.Edit", [ref_field(),
    field("pointer", "Pointer", "نشانی", required=True, maxLength=300, placeholder=t("/columns/0/header", "/columns/0/header")),
    field("value", "Value (JSON)", "مقدار (JSON)", "textArea", required=True, maxLength=100000, span=2)])
form("designer-action-create", "Create an action from an operation", "ایجاد کنش از یک عملیات", "Create", "ایجاد", "Definitions.Create", [
    field("key", "Action key", "کلید کنش", required=True, maxLength=200), field("appKey", "App", "برنامه", maxLength=100),
    field("model", "Operation model", "مدل عملیات", required=True, maxLength=100, placeholder=t("see the operation list", "فهرست عملیات را ببینید")), field("action", "Operation action", "کنش عملیات", required=True, maxLength=100),
    field("args", "Arguments (JSON: name to field:x, literal:x or context:x)", "آرگومان‌ها (JSON)", "textArea", maxLength=100000, span=2),
    field("successEn", "Success message (English)", "پیام موفقیت (انگلیسی)", required=True, maxLength=300), field("successFa", "Success message (Persian)", "پیام موفقیت (فارسی)", required=True, maxLength=300),
    field("redirect", "Redirect after success", "مسیر پس از موفقیت", maxLength=300)])

# each form posts to its action
for form_key, action_key in (("designer-form-field", "designer-add-form-field"), ("designer-page-instance", "designer-add-page-instance"), ("designer-menu-item", "designer-add-menu-item"),
                             ("designer-set-value", "designer-set-value"), ("designer-action-create", "designer-create-action")):
    path = os.path.join(OUT, "form", "core", form_key + ".json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    d["payload"]["actionKey"] = action_key
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)

pay_cols = [{"key": "index", "header": t("#", "#"), "width": 70}, {"key": "summary", "header": t("Part", "بخش"), "width": 260}, {"key": "json", "header": t("Value", "مقدار"), "width": 480},
            {"key": "_actions", "header": t("Actions", "عملیات"), "type": "actions", "width": 300, "sortable": False, "filterable": False, "actions": [
                {"key": "up", "label": t("Up", "بالا"), "url": "/a/designer-move-up", "method": "post"},
                {"key": "down", "label": t("Down", "پایین"), "url": "/a/designer-move-down", "method": "post"},
                {"key": "remove", "label": t("Remove", "حذف"), "url": "/a/designer-remove-row", "method": "post", "tone": "danger", "confirm": t("Remove this part from the draft?", "این بخش از پیش‌نویس حذف شود؟")}]}]


def designer_page(key, title_en, title_fa, pointer, new_url, new_label):
    base = "/d/definition-payload-grid?f.ref=eq:{{params.ref|url}}&f.pointer=eq:"
    url = base + (pointer if pointer else "{{params.pointer|url}}")
    write("page", key, t(title_en, title_fa), t(title_en, title_fa), {"layout": "list-page", "route": key, "permission": "Definitions.View", "template": "entity-list",
                                                                      "args": {"id": key, "url": url, "idField": "rowId", "newUrl": new_url, "newLabel": new_label, "columns": pay_cols}})


designer_page("designer", "Definition designer (open it with ?ref=appKey:kind:key&pointer=/fields)", "طراح تعریف", None, "/f/designer-set-value", t("Set a value", "تنظیم مقدار"))
designer_page("designer-form", "Form designer: fields (open it with ?ref=appKey:kind:key)", "طراح فرم: فیلدها", "/fields", "/f/designer-form-field", t("Add a field", "افزودن فیلد"))
designer_page("designer-page", "Page designer: regions (open it with ?ref=appKey:kind:key)", "طراح صفحه: نواحی", "/regions", "/f/designer-page-instance", t("Add a component", "افزودن مؤلفه"))
designer_page("designer-menu", "Menu designer: items (open it with ?ref=appKey:kind:key)", "طراح منو: آیتم‌ها", "/items", "/f/designer-menu-item", t("Add an item", "افزودن آیتم"))

op_cols = [{"key": "ref", "header": t("Operation", "عملیات"), "width": 300}, {"key": "permission", "header": t("Permission", "مجوز"), "width": 220}, {"key": "arguments", "header": t("Arguments", "آرگومان‌ها"), "width": 480}]
write("page", "designer-actions", t("Action designer: operations", "طراح کنش: عملیات‌ها"), t("Every operation an action can run, with its permission and arguments", "همه عملیات‌ها"),
      {"layout": "list-page", "route": "designer-actions", "permission": "Definitions.View", "template": "entity-list",
       "args": {"id": "designer-actions", "url": "/d/operation-catalog-grid", "idField": "ref", "newUrl": "/f/designer-action-create", "newLabel": t("Create an action", "ایجاد کنش"), "columns": op_cols}})
print("designers ok")
