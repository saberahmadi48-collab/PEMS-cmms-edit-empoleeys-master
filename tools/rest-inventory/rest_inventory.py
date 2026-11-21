#!/usr/bin/env python3
"""REST inventory of the MVC controllers (plan row #432, ADR-0009).

Static scan of PEMS.Web/**/Controllers/*Controller.cs (PEMS.Web/Api, the REST controllers, is skipped): one row per public action with its verb, return kind, authorization signal and a
proposed target resource. It does not compile anything and never edits code. Run from the repository root:

    python3 tools/rest-inventory/rest_inventory.py            # writes docs/audits/rest-inventory.csv and prints a summary
    python3 tools/rest-inventory/rest_inventory.py --check    # exit 1 if the committed CSV differs from a fresh scan

Limits (static scan, not reflection): attribute routes on the class are read, convention routes are assumed; an action is "json" when its body
returns Json/Ok/Content/StatusCode objects, "page" when it returns View/PartialView, "redirect" for Redirect*/RedirectToAction, "file" for File.
The permission signal is "attribute" ([Authorize(Policy/Roles)] on class or method), "inline" (body mentions an access-control call) or "none"
(only the global signed-in requirement from AuthorizeFilter applies).
"""
import csv, glob, os, re, sys, collections

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "docs", "audits", "rest-inventory.csv")
VERBS = ("Get", "Post", "Put", "Patch", "Delete")
STATE_WORDS = re.compile(r"(Delete|Remove|Create|Add|Save|Update|Edit|Approve|Reject|Submit|Cancel|Toggle|Activate|Deactivate|Close|Reopen|Assign|Release|Complete|Import|Upload|Reset|Send|Mark|Set|Move|Clone|Publish|Archive|Restore)", re.I)
INLINE_ACCESS = re.compile(r"(AccessControl|HasPermission|RequirePermission|CanAccess|IsInRole|Forbid\(|ChallengeAsync|_authz|_access\.|Permission)")
WAVES = [
    (re.compile(r"/Areas/Admin/|^(Account|AccountRecovery|BreakGlass|DashboardSettings|Home|Help|WebAuthn|Notification|NavigationBadge|GlobalSearch|QuickSetup|Error)", re.I), "W1 admin, account, shell"),
    (re.compile(r"(WorkOrder|Maintenance|Equipment|Asset|Cmms|Criticality|FailureMode|Finding|Reliability|Inspection|Instrument|MechanicalEquipment|FunctionalLocation|Condition|PM|Crew|Team|Employee|ShiftHandover|Permit|Permits|WorkRequest|Task)", re.I), "W2 CMMS, maintenance, reliability"),
    (re.compile(r"(Document|VDR|Letter|DCC|Import|AI|OCR|Transmittal|TQ|Revision|Engineering|MDB|Barcode|Relationship)", re.I), "W3 engineering documents, letters, import, AI"),
    (re.compile(r"/Areas/Warehouse/|(Procurement|Inventory|Warehouse|Material|Vendor|Contract|Requisition|PartCatalogue|Replenishment|Store|Stock)", re.I), "W4 procurement, inventory, warehouse"),
]

def wave_of(path, name):
    key = path.replace("\\", "/") + "/" + name
    for rx, w in WAVES:
        if rx.search(key): return w
    return "W5 operations, production, organization, other"

def kebab(s):
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", s)
    return s.lower()

def plural(s):
    if s.endswith("y") and not s.endswith(("ay", "ey", "oy", "uy")): return s[:-1] + "ies"
    if s.endswith(("s", "x", "ch", "sh")): return s + "es"
    return s + "s"

def skip_literal(t, i):
    """Return the index after the string/char literal or comment starting at i, or i if none starts here."""
    n = len(t)
    c = t[i]
    if t.startswith("//", i):
        k = t.find("\n", i); return n if k < 0 else k
    if t.startswith("/*", i):
        k = t.find("*/", i + 2); return n if k < 0 else k + 2
    if c in "$@" and i + 1 < n and t[i + 1] in '"$@' or c == '"':
        j = i
        verbatim = False
        while j < n and t[j] in "$@": 
            if t[j] == "@": verbatim = True
            j += 1
        if j < n and t[j] == '"':
            if t.startswith('"""', j):
                k = t.find('"""', j + 3); return n if k < 0 else k + 3
            j += 1
            while j < n:
                if verbatim:
                    if t[j] == '"' and j + 1 < n and t[j + 1] == '"': j += 2; continue
                    if t[j] == '"': return j + 1
                else:
                    if t[j] == "\\": j += 2; continue
                    if t[j] == '"': return j + 1
                j += 1
            return n
        return i
    if c == "'":
        j = i + 1
        while j < n and t[j] != "'":
            j += 2 if t[j] == "\\" else 1
        return min(j + 1, n)
    return i

def match_brace(text, start):
    depth = 0
    i = start
    n = len(text)
    while i < n:
        k = skip_literal(text, i)
        if k != i: i = k; continue
        c = text[i]
        if c == "{": depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0: return i
        i += 1
    return n - 1

CLASS_RX = re.compile(r"((?:\[[^\]]*\]\s*)*)\s*public\s+(?:sealed\s+|partial\s+|abstract\s+)*class\s+(\w+Controller)\b[^{;]*\{", re.S)

def members(body):
    """Yield (header, text_of_body) for each member declared at depth 0 of a class body."""
    n = len(body); i = 0; start = 0
    while i < n:
        k = skip_literal(body, i)
        if k != i: i = k; continue
        c = body[i]
        if c == "[":
            # attribute list: skip to the matching ] so commas/parens inside do not confuse the header
            depth = 0
            while i < n:
                k = skip_literal(body, i)
                if k != i: i = k; continue
                if body[i] == "[": depth += 1
                elif body[i] == "]":
                    depth -= 1
                    if depth == 0: i += 1; break
                i += 1
            continue
        if c == "(":
            depth = 0
            while i < n:
                k = skip_literal(body, i)
                if k != i: i = k; continue
                if body[i] == "(": depth += 1
                elif body[i] == ")":
                    depth -= 1
                    if depth == 0: i += 1; break
                i += 1
            continue
        if c == "{":
            end = match_brace(body, i)
            yield body[start:i], body[i + 1:end]
            i = end + 1; start = i; continue
        if body.startswith("=>", i):
            j = i + 2; depth = 0
            while j < n:
                k = skip_literal(body, j)
                if k != j: j = k; continue
                if body[j] in "({[": depth += 1
                elif body[j] in ")}]": depth -= 1
                elif body[j] == ";" and depth <= 0: break
                j += 1
            yield body[start:i], body[i + 2:j]
            i = j + 1; start = i; continue
        if c == ";":
            i += 1; start = i; continue
        i += 1

HEADER_RX = re.compile(r"public\s+(?:async\s+|virtual\s+|override\s+)*([\w<>\[\],\.\?\s]+?)\s+(\w+)\s*\(")

def scan_file(path):
    text = open(path, encoding="utf-8-sig", errors="ignore").read()
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    rows = []
    for cm in CLASS_RX.finditer(text):
        cattrs, cname = cm.group(1), cm.group(2)
        cend = match_brace(text, cm.end() - 1)
        body = text[cm.end(): cend]
        class_anon = "AllowAnonymous" in cattrs
        class_authorize = "Authorize" in cattrs and not class_anon
        area = re.search(r"/Areas/(\w+)/", rel)
        ctrl = cname[:-len("Controller")]
        for header, mbody in members(body):
            hm = HEADER_RX.search(header)
            if not hm: continue
            ret, name = " ".join(hm.group(1).split()), hm.group(2)
            attrs = header[:hm.start()]
            if name == cname or "NonAction" in attrs or " class " in " " + ret + " ": continue
            if not re.search(r"(IActionResult|ActionResult|JsonResult|ViewResult|FileResult|ContentResult|PartialViewResult|RedirectResult|ObjectResult|IResult)", ret): continue
            verbs = [v.upper() for v in VERBS if re.search(r"\[\s*Http" + v + r"\b|,\s*Http" + v + r"\b", attrs)]
            verb = verbs[0] if verbs else "ANY"
            route = re.search(r"Http\w+\(\"([^\"]*)\"", attrs)
            kinds = []
            if re.search(r"\b(Json|Ok|Content|StatusCode|BadRequest|NotFound|Problem|Accepted|Created|Run)\s*\(", mbody) and not re.search(r"\b(View|PartialView)\s*\(", mbody): kinds.append("json")
            if re.search(r"\b(View|PartialView)\s*\(", mbody) or "ViewResult" in ret: kinds.append("page")
            if re.search(r"\bRedirect\w*\s*\(", mbody): kinds.append("redirect")
            if re.search(r"\b(File|PhysicalFile|VirtualFile)\s*\(", mbody) or "FileResult" in ret: kinds.append("file")
            if "JsonResult" in ret and "json" not in kinds: kinds.append("json")
            kind = "+".join(kinds) if kinds else "other"
            anon = class_anon or "AllowAnonymous" in attrs
            attr_auth = class_authorize or "Authorize" in attrs
            inline = bool(INLINE_ACCESS.search(mbody))
            signal = "anonymous" if anon else "attribute" if attr_auth else "inline" if inline else "none"
            csrf = "ValidateAntiForgeryToken" in attrs or "AutoValidateAntiforgeryToken" in cattrs
            state_get = verb in ("GET", "ANY") and bool(STATE_WORDS.match(name)) and not name.startswith(("Get", "Search", "Index", "List", "Export", "Download", "Details", "View", "Create", "Edit", "New"))
            verb_in_name = bool(STATE_WORDS.match(name))
            rows.append(dict(file=rel, area=area.group(1) if area else "", controller=ctrl, action=name, verb=verb, route=route.group(1) if route else "",
                             returns=ret, kind=kind, permission=signal, antiforgery="yes" if csrf else "no", state_changing_get="yes" if state_get else "no", verb_in_name="yes" if verb_in_name else "no"))
    return rows

def propose(r):
    res = plural(kebab(r["controller"]))
    a, verb, kind = r["action"], r["verb"], r["kind"]
    ak = kebab(a)
    if kind.startswith("page") and verb in ("GET", "ANY") and "json" not in kind:
        return "stays", "page (stays MVC)", ""
    if a in ("Index", "List", "Search", "Browse"): return "resource", "GET /" + res, "list"
    if a in ("Details", "Detail", "View", "Show", "Get"): return "resource", "GET /" + res + "/{id}", "read"
    if a.startswith("Create") or a in ("New", "Add") or (a == "Create"): return "resource", ("POST /" + res if verb == "POST" else "page (form)"), "create"
    if a.startswith("Edit") or a.startswith("Update"): return "resource", ("PATCH /" + res + "/{id}" if verb == "POST" else "page (form)"), "update"
    if a.startswith(("Delete", "Remove")): return "resource", "DELETE /" + res + "/{id}", "delete"
    if "file" in kind: return "resource", "GET /" + res + "/{id}/" + ak, "download"
    if verb == "POST": return "resource", "POST /" + res + "/{id}/actions/" + ak, "action"
    if "json" in kind: return "resource", "GET /" + res + "/" + ak, "query"
    return "stays", "page (stays MVC)", ""

def main():
    files = sorted(glob.glob(os.path.join(ROOT, "PEMS.Web", "**", "*Controller.cs"), recursive=True))
    # PEMS.Web/Api holds the REST controllers (SCRUM-435): they are the target of the migration, not part of the map of what still has to move.
    files = [f for f in files if os.sep + "Api" + os.sep not in os.path.relpath(f, os.path.join(ROOT, "PEMS.Web")).join([os.sep, ""])]
    files = [f for f in files if "/obj/" not in f.replace("\\", "/") and "/bin/" not in f.replace("\\", "/")]
    rows = []
    for f in files: rows += scan_file(f)
    posts = {(r["file"], r["action"]) for r in rows if r["verb"] == "POST"}
    for r in rows:
        # a GET that only shows a form for a POST of the same name is not a state change
        if r["state_changing_get"] == "yes" and ((r["file"], r["action"]) in posts or "page" in r["kind"]):
            r["state_changing_get"] = "no"
    for r in rows:
        cls, target, op = propose(r)
        r["class"] = "view-only MVC" if cls == "stays" and r["kind"].startswith("page") else \
                     "JSON endpoint" if "json" in r["kind"] else \
                     "form post" if r["verb"] == "POST" else \
                     "download" if "file" in r["kind"] else "other"
        r["target"] = target; r["operation"] = op
        r["wave"] = wave_of(r["file"], r["controller"])
    cols = ["area", "controller", "action", "verb", "route", "returns", "kind", "class", "operation", "target", "permission", "antiforgery", "state_changing_get", "verb_in_name", "wave", "file"]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    tmp = []
    for r in sorted(rows, key=lambda x: (x["file"], x["action"])): tmp.append([r[c] for c in cols])
    import io
    buf = io.StringIO(newline="")
    w = csv.writer(buf, lineterminator="\n"); w.writerow(cols); w.writerows(tmp)
    data = buf.getvalue()
    if "--check" in sys.argv:
        cur = open(OUT, encoding="utf-8", newline="").read() if os.path.exists(OUT) else ""
        if cur.replace("\r\n", "\n") != data:
            print("rest-inventory.csv is out of date; run tools/rest-inventory/rest_inventory.py"); return 1
        print("rest-inventory.csv is current"); return 0
    open(OUT, "w", encoding="utf-8", newline="").write(data)
    ctrls = collections.Counter(r["controller"] + "|" + r["area"] for r in rows)
    print(f"controllers: {len(ctrls)}  actions: {len(rows)}")
    print("verbs:", dict(collections.Counter(r["verb"] for r in rows)))
    print("class:", dict(collections.Counter(r["class"] for r in rows)))
    print("permission signal:", dict(collections.Counter(r["permission"] for r in rows)))
    print("state-changing GET/ANY:", sum(1 for r in rows if r["state_changing_get"] == "yes"))
    print("POST without antiforgery:", sum(1 for r in rows if r["verb"] == "POST" and r["antiforgery"] == "no"))
    print("json controllers:", len({r["controller"] + r["area"] for r in rows if "json" in r["kind"]}))
    return 0

if __name__ == "__main__":
    sys.exit(main())
