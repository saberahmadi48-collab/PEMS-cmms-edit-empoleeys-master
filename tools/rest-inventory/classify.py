#!/usr/bin/env python3
"""Migration classification of every controller (plan row #85, SCRUM-106).

One row per controller (area + name) in plan/migration-classification.csv with exactly one wave, one owner and one decision:

    page-definition   a list/detail/dashboard page that a Page definition (SCRUM-438, /p/{key}) can express with the registered components
    form-definition   create/edit pages that the form engine (SCRUM-578, /f/{key}) can express; the save is a registered action handler
    workflow-hybrid   keeps its own approval/routing tables (with the reason in the notes) or moves to IWorkflowRuntime later
    code-only         files, imports, exports, integrations, authentication, AI, machine endpoints: stays code

The scan is the REST inventory (tools/rest-inventory/rest_inventory.py, docs/audits/rest-inventory.csv): the classes of its actions give the proposal. A row that is already in the file is never changed by
the tool (the owner's decision wins); only controllers without a row are added, as "proposed". Run from the repository root:

    python3 tools/rest-inventory/classify.py --write   # adds the missing controllers as proposals
    python3 tools/rest-inventory/classify.py --check   # exit 1 if a controller has no row, a row has no controller, or a row is incomplete
"""
import csv, os, re, sys, collections

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
INVENTORY = os.path.join(ROOT, "docs", "audits", "rest-inventory.csv")
OUT = os.path.join(ROOT, "plan", "migration-classification.csv")
FIELDS = ["area", "controller", "module", "wave", "owner", "decision", "status", "notes"]
DECISIONS = {"page-definition", "form-definition", "workflow-hybrid", "code-only"}

HYBRID = re.compile(r"(Letter|Workflow|Approval|Permit|MOC|Contract|Procurement|PurchaseOrder|Requisition|Transmittal|TechnicalQuery|DocumentWorkflow|Referral|Cartable|MyWork|IDC|Authority)", re.I)
CODE_ONLY = re.compile(r"(Account|WebAuthn|BreakGlass|Auth|Security|License|Storage|DatabaseUpdate|Import|Export|Upload|Barcode|OCR|Ai\b|AI|Chat|GlobalSearch|Search|Notification|NavigationBadge|Error|Help|Backup|Health|Probe|Settings|Numbering|Routing|Sod|Audit|Sidebar|Menu|Navigation|Dashboard|Report|Chart|Print|Download|File|Attachment)", re.I)
MODULES = [
    (re.compile(r"/Areas/Admin/", re.I), "Administration"),
    (re.compile(r"(WorkOrder|WorkRequest|Maintenance|PM|Strategy|Inspection|Condition|Reliability|Failure|Finding|Criticality)", re.I), "Maintenance and reliability"),
    (re.compile(r"(Equipment|Asset|FunctionalLocation|Instrument|Mechanical)", re.I), "Assets"),
    (re.compile(r"(Permit|MOC|Isolation|GasTest|Simops)", re.I), "Permit to work and safety"),
    (re.compile(r"(Document|VDR|DCC|Transmittal|TechnicalQuery|MDB|Revision|Engineering)", re.I), "Engineering documents"),
    (re.compile(r"(Letter|Correspondence)", re.I), "Correspondence"),
    (re.compile(r"(Procurement|PurchaseOrder|Contract|Vendor|Requisition|Warehouse|Inventory|Material|Stock|PartCatalogue|Mrs|Irn)", re.I), "Procurement and warehouse"),
    (re.compile(r"(Employee|Department|Organization|Position|Crew|Team|Role|User|Shift)", re.I), "Organization and people"),
    (re.compile(r"(Task|Cartable|MyWork|Referral|Notification)", re.I), "Work and tasks"),
]


def module_of(area, name):
    key = f"/Areas/{area}/" + name if area else name
    for rx, m in MODULES:
        if rx.search(key): return m
    return "Platform and shell"


def read_inventory():
    with open(INVENTORY, encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    by = collections.OrderedDict()
    for r in rows:
        by.setdefault((r["area"], r["controller"]), []).append(r)
    return by


def propose(area, name, actions):
    classes = collections.Counter(a["kind"] for a in actions)
    state = sum(1 for a in actions if a["verb"] in ("POST", "PUT", "PATCH", "DELETE"))
    pages = classes.get("page", 0)
    machine = classes.get("json", 0) + classes.get("file", 0)
    if HYBRID.search(name):
        return "workflow-hybrid", "own approval or routing tables; decide per module whether it moves to IWorkflowRuntime"
    if CODE_ONLY.search(name) or machine > pages:
        return "code-only", "files, imports, integrations, machine endpoints or shell behaviour"
    if pages and state:
        return "form-definition", "list and detail pages plus create/edit: page definitions and form definitions"
    if pages:
        return "page-definition", "view-only pages: page definitions"
    return "code-only", "no page actions"


def read_rows():
    if not os.path.exists(OUT): return []
    with open(OUT, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def check(inv, rows):
    problems = []
    seen = collections.Counter((r["area"], r["controller"]) for r in rows)
    for key in inv:
        if seen[key] == 0: problems.append(f"controller {key[0] + '/' if key[0] else ''}{key[1]} has no row")
    for key, n in seen.items():
        if key not in inv: problems.append(f"row {key[0] + '/' if key[0] else ''}{key[1]} has no controller")
        if n > 1: problems.append(f"controller {key[0] + '/' if key[0] else ''}{key[1]} has {n} rows (exactly one)")
    for r in rows:
        for f in ("module", "wave", "owner", "decision"):
            if not (r.get(f) or "").strip(): problems.append(f"row {r['controller']}: {f} is empty")
        if r.get("decision") not in DECISIONS: problems.append(f"row {r['controller']}: decision '{r.get('decision')}' is not one of {sorted(DECISIONS)}")
    return problems


def main(argv):
    inv = read_inventory()
    rows = read_rows()
    if "--write" in argv:
        have = {(r["area"], r["controller"]) for r in rows}
        added = 0
        for (area, name), actions in inv.items():
            if (area, name) in have: continue
            decision, notes = propose(area, name, actions)
            rows.append({"area": area, "controller": name, "module": module_of(area, name), "wave": actions[0]["wave"], "owner": "Hamid", "decision": decision, "status": "proposed", "notes": notes})
            added += 1
        rows = [r for r in rows if (r["area"], r["controller"]) in inv]
        rows.sort(key=lambda r: (r["wave"], r["module"], r["area"], r["controller"]))
        with open(OUT, "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=FIELDS, lineterminator="\n")
            w.writeheader()
            w.writerows(rows)
        print(f"wrote {OUT}: {len(rows)} controllers, {added} added as proposals")
        counts = collections.Counter(r["decision"] for r in rows)
        for d, n in counts.most_common(): print(f"  {d}: {n}")
    problems = check(inv, read_rows())
    if "--check" in argv or "--write" in argv:
        for p in problems: print("PROBLEM:", p)
        if problems: sys.exit(1)
        print(f"classification ok: {len(inv)} controllers, each with one wave, one owner and one decision")


if __name__ == "__main__":
    main(sys.argv[1:])
