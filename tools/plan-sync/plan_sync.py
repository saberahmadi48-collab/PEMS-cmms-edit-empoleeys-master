#!/usr/bin/env python3
"""Plan as markdown files -> tracker (Jira) import files, checks, summary and cheap edits.   (see docs/process/PLAN_TRACKER_WORKFLOW.md)

    python tools/plan-sync/plan_sync.py check                          # offline consistency checks on plan/rows (exit 1 on findings)
    python tools/plan-sync/plan_sync.py summary                        # counts by status, priority, area, group, milestone (was the Summary sheet)
    python tools/plan-sync/plan_sync.py show 435                       # print one row
    python tools/plan-sync/plan_sync.py set 435 status="In Progress"   # change front matter fields of one row (validated)
    python tools/plan-sync/plan_sync.py report 435 "text"              # replace the Report section ("-" reads stdin; "+text" appends a paragraph)
    python tools/plan-sync/plan_sync.py new 447 "Task title" area="..." group="..."   # new row file from the template (id = Jira key number)
    python tools/plan-sync/plan_sync.py export                         # plan/jira-import.csv + plan/jira-links.csv (git-ignored, for the Jira import)
    python tools/plan-sync/plan_sync.py reconcile [--csv F | --json F]     # tracker vs plan: status differences, rows without issue, issues without row/Plan ID, duplicate titles
    python tools/plan-sync/plan_sync.py pull [--dry-run] [--csv F | --json F]   # write the tracker status into the plan rows (Jira owns status)
    python tools/plan-sync/plan_sync.py tracker-check [--csv F | --json F]  # exit 1 on an issue without Plan ID/plan-N label, a duplicate title, a row/issue without partner
    python tools/plan-sync/plan_sync.py push [--apply] [--csv F | --json F]    # plan -> tracker: summary, priority, plan-managed labels (dry run unless --apply)

The plan is one markdown file per backlog row in plan/rows (front matter fields, then Evidence, Prompt, Requirement, Report sections) and
text pages for the other former Excel sheets in plan/sheets. Edit a file directly, or use set/report/new; both end in the same files.
The plan is tracker-free; everything tracker-specific (field names, sprint import, assignees) lives in this tool.
reconcile/pull/tracker-check/push read the issues from the Jira REST API, or offline from a Jira CSV export (`--csv`) or a saved JSON list (`--json`).
The API credentials are CI secrets / environment variables (JIRA_BASE_URL, JIRA_PROJECT_KEY, JIRA_USER_EMAIL, JIRA_API_TOKEN), never files in git
and never printed. Tests run against a fake transport, no live call.
Assignees come from plan/members.json ({"Member A": "a@company.com", ...}); copy plan/members.example.json. The real file is git-ignored.
"""
import csv, json, os, re, sys, time, urllib.error, urllib.request
from base64 import b64encode
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROWS_DIR = ROOT / "plan" / "rows"
SHEETS_DIR = ROOT / "plan" / "sheets"
OUT_DIR = ROOT / "plan"
MEMBERS = OUT_DIR / "members.json"

ISSUE_TYPE = {"Feature": "Story", "Bug Fix": "Bug"}                    # everything else -> Task
PRIORITY = {"P0": "Highest", "P1": "High", "P2": "Medium", "P3": "Low"}
STATUS = {"Not Started": "To Do", "In Progress": "In Progress", "Partial": "In Progress", "Done": "Done",
          "Hold": "On Hold", "Later": "Backlog", "Cancelled": "Won't Do", "Obsolete": "Won't Do"}
EPICS = {"M0": "M0 Baseline and foundation decisions", "M1": "M1 Dynamic core, components and licensing (Alpha)",
         "M2": "M2 Builders, demo and install (Beta)", "M3": "M3 Commercial release 1.0",
         "M4": "M4 Platform completion and module migration", "M5": "M5 Competitive features",
         "M6": "M6 Later", "-": "No milestone"}
LABEL_COLUMNS = 6


FIELDS = [("#", "id"), ("Task", "task"), ("Doer", "doer"), ("Area", "area"), ("Track", "track"), ("Status", "status"),
          ("Priority", "priority"), ("Milestone", "milestone"), ("Effort", "effort"), ("Commercial", "commercial"),
          ("Depends", "depends"), ("Reference (Titan)", "reference_titan"), ("Old Ref", "old_ref"), ("Group", "group"),
          ("Confirmed by PM", "confirmed_by_pm"), ("Sprint", "sprint"), ("Owner", "owner")]
SECTIONS = ["Evidence", "Prompt", "Requirement", "Report"]
HEADER = [h for h, _ in FIELDS[:1]] + ["Doer", "Area", "Task", "Track", "Status", "Priority", "Evidence", "Prompt", "Requirement", "Report",
                                       "Milestone", "Effort", "Commercial", "Depends", "Reference (Titan)", "Old Ref", "Group",
                                       "Confirmed by PM", "Sprint", "Owner"]
KEY_OF = dict(FIELDS)
NAME_OF = {k: h for h, k in FIELDS}
SECTION_RE = re.compile(r"(?m)^## (" + "|".join(SECTIONS) + r")[ \t]*$")


def row_path(pid):
    return ROWS_DIR / f"{int(pid):04d}.md"


def parse_row(text, source="row"):
    """One row file: `---` front matter (key: value lines), then `## Evidence|Prompt|Requirement|Report` sections."""
    lines = text.replace("\r\n", "\n").split("\n")
    if not lines or lines[0].strip() != "---":
        raise ValueError(f"{source}: front matter must start with ---")
    end = next((i for i in range(1, len(lines)) if lines[i].strip() == "---"), None)
    if end is None:
        raise ValueError(f"{source}: front matter is not closed")
    row = {}
    for ln in lines[1:end]:
        if not ln.strip():
            continue
        key, sep, value = ln.partition(":")
        key = key.strip()
        if not sep or key not in NAME_OF:
            raise ValueError(f"{source}: unknown or malformed front matter line '{ln}'")
        row[NAME_OF[key]] = value.strip()
    body = "\n".join(lines[end + 1:])
    parts = SECTION_RE.split(body)                       # [preamble, name, text, name, text, ...]
    if parts[0].strip():
        raise ValueError(f"{source}: text before the first section")
    for name, content in zip(parts[1::2], parts[2::2]):
        row[name] = content.strip()
    row["#"] = int(row["#"])
    return row


def format_row(row):
    out = ["---"]
    for h, k in FIELDS:
        v = clean(row.get(h))
        if v:
            out.append(f"{k}: {v}")
    out.append("---")
    for name in SECTIONS:
        v = clean(row.get(name))
        if v:
            out += ["", f"## {name}", "", v]
    return "\n".join(out) + "\n"


def load_rows():
    rows = []
    for path in sorted(ROWS_DIR.glob("*.md")):
        row = parse_row(path.read_text(encoding="utf-8"), path.name)
        if path.name != f"{row['#']:04d}.md":
            raise ValueError(f"{path.name}: file name does not match id {row['#']}")
        rows.append(row)
    return HEADER, sorted(rows, key=lambda r: r["#"])


def clean(v):
    return "" if v is None else str(v).strip()


def load_members():
    if MEMBERS.exists():
        return json.loads(MEMBERS.read_text(encoding="utf-8"))
    return {}


def sprint_of(r):
    s = clean(r.get("Sprint"))
    return "" if not s or "stretch" in s.lower() else s


def labels(r):
    out = []
    if clean(r.get("Effort")): out.append("effort-" + clean(r["Effort"]))
    if clean(r.get("Doer")): out.append("doer-" + clean(r["Doer"]).lower())
    if clean(r.get("Confirmed by PM")).lower() == "yes": out.append("pm-confirmed")
    if clean(r.get("Status")) == "Partial": out.append("partial")
    if clean(r.get("Status")) == "Later": out.append("later")
    if clean(r.get("Commercial")) == "Sales blocker": out.append("sales-blocker")
    if "stretch" in clean(r.get("Sprint")).lower(): out.append("sprint-1-stretch")
    # team-managed Jira spaces have no Components: the plan area travels as a label instead
    area = re.sub(r"[^a-z0-9]+", "-", clean(r.get("Area")).lower()).strip("-")
    if area: out.append("area-" + area[:60])
    return out


def summary(r):
    return f"[#{r['#']}] {clean(r.get('Task'))}"


def description(r, titles):
    head = (f"*Plan ID:* #{r['#']}   *Area:* {clean(r.get('Area'))}   *Group:* {clean(r.get('Group'))}   "
            f"*Milestone:* {clean(r.get('Milestone')) or '-'}   *Priority:* {clean(r.get('Priority'))}   "
            f"*Effort:* {clean(r.get('Effort'))}   *Owner:* {clean(r.get('Owner')) or '-'}   *Doer:* {clean(r.get('Doer'))}")
    parts = [head, "*Step 0:* read AGENTS.md and docs/guidelines/README.md, then the related Jira issues, before starting."]
    for title, key in (("Why", "Evidence"), ("What to do", "Prompt"), ("Done when", "Requirement")):
        if clean(r.get(key)):
            text = clean(r[key])
            if key == "Requirement" and text.lower().startswith("done when:"):
                text = text[len("done when:"):].strip()          # the section header already says "Done when"
            parts.append(f"h3. {title}\n{text}")
    deps = re.findall(r"#(\d+)", clean(r.get("Depends")))
    if deps:
        parts.append("h3. Depends on\n" + "\n".join(f"* [#{d}] {titles.get(int(d), '(unknown)')}" for d in deps))
    if clean(r.get("Report")):
        parts.append(f"h3. Report\n{clean(r['Report'])}")
    return "\n\n".join(parts)


def export():
    header, rows = load_rows()
    OUT_DIR.mkdir(exist_ok=True)
    members = load_members()
    titles = {int(r["#"]): clean(r.get("Task")) for r in rows}

    milestones = sorted({clean(r.get("Milestone")) or "-" for r in rows})
    with (OUT_DIR / "jira-import.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["Work item ID", "Work type", "Parent", "Summary", "Description", "Priority", "Status", "Fix Version", "Plan ID",
                    "Sprint", "Assignee"] + ["Labels"] * LABEL_COLUMNS)                  # repeated header = multi-value Labels
        epic_id = {}
        for i, m in enumerate(milestones, start=1):                                     # epics first (parents before children)
            epic_id[m] = i
            w.writerow([i, "Epic", "", EPICS.get(m, m), f"Milestone {m}", "", "", m, "", "", ""] + [""] * LABEL_COLUMNS)
        next_id = len(milestones) + 1
        for r in rows:
            m = clean(r.get("Milestone")) or "-"
            lab = (labels(r) + [""] * LABEL_COLUMNS)[:LABEL_COLUMNS]
            w.writerow([next_id, ISSUE_TYPE.get(clean(r.get("Track")), "Task"), epic_id[m], summary(r), description(r, titles),
                        PRIORITY.get(clean(r.get("Priority")), "Medium"), STATUS.get(clean(r.get("Status")), "To Do"), m, r["#"],
                        sprint_of(r), members.get(clean(r.get("Owner")), "")] + lab)
            next_id += 1

    with (OUT_DIR / "jira-links.csv").open("w", newline="", encoding="utf-8") as fh:     # for the API step (links are not CSV-importable)
        w = csv.writer(fh)
        w.writerow(["plan_id", "blocked_by_plan_id"])
        for r in rows:
            for dep in re.findall(r"#(\d+)", clean(r.get("Depends"))):
                w.writerow([r["#"], dep])

    in_sprint = sum(1 for r in rows if sprint_of(r))
    print(f"exported {len(rows)} rows: plan/jira-import.csv ({len(rows) + len(milestones)} work items, "
          f"{in_sprint} in the sprint), plan/jira-links.csv")
    if not members:
        print("note: plan/members.json not found - Assignee left empty (copy plan/members.example.json)")
    if len(rows) + len(milestones) > 1500:
        print("note: Jira recommends at most ~1,500 work items per import file; split before importing")


def dependency_findings(rows):
    """Plan audit 2026-10-28: a row must not depend on a row planned in a LATER milestone, and dependencies must not form a cycle."""
    def stage(r):
        m = re.fullmatch(r"M(\d)", clean(r.get("Milestone")))
        return int(m.group(1)) if m else None
    by_id = {r["#"]: r for r in rows}
    deps = {r["#"]: [d for d in (int(x) for x in re.findall(r"#(\d+)", clean(r.get("Depends")))) if d in by_id and d != r["#"]] for r in rows}
    out = []
    for pid, ds in deps.items():
        for d in ds:
            a, b = stage(by_id[pid]), stage(by_id[d])
            if a is not None and b is not None and b > a:
                out.append(f"#{pid}: planned in M{a} but depends on #{d} planned in M{b}")
    state = {}
    def visit(n, path):
        state[n] = 1
        for d in deps[n]:
            if state.get(d) == 1:
                out.append("dependency cycle: " + " -> ".join(f"#{x}" for x in path + [n, d]))
            elif d not in state:
                visit(d, path + [n])
        state[n] = 2
    for n in deps:
        if n not in state:
            visit(n, [])
    return out


def check():
    _, rows = load_rows()
    problems = []
    ids = Counter(r["#"] for r in rows)
    problems += [f"duplicate Plan ID {i}" for i, n in ids.items() if n > 1]
    known = set(ids)
    for r in rows:
        pid = r["#"]
        status, evidence = clean(r.get("Status")), clean(r.get("Evidence"))
        # "Existing capabilities" rows are estimated from code (a class name is their evidence) and are listed for QA via the Test Plan
        if status == "Done" and len(evidence) < 20 and clean(r.get("Group")) != "Existing capabilities":
            problems.append(f"#{pid}: Done without evidence")
        if status not in STATUS:
            problems.append(f"#{pid}: unknown status '{status}'")
        if clean(r.get("Priority")) not in PRIORITY:
            problems.append(f"#{pid}: unknown priority '{clean(r.get('Priority'))}'")
        for dep in re.findall(r"#(\d+)", clean(r.get("Depends"))):
            if int(dep) not in known:
                problems.append(f"#{pid}: depends on unknown #{dep}")
        if clean(r.get("Sprint")):                                  # a planned row must be unambiguous
            if not clean(r.get("Owner")):
                problems.append(f"#{pid}: planned in {clean(r.get('Sprint'))} without an Owner")
            if len(clean(r.get("Prompt"))) < 60 or not clean(r.get("Requirement")).lower().startswith("done when"):
                problems.append(f"#{pid}: planned in a sprint but 'What to do' is short or 'Done when' is missing")
    problems += dependency_findings(rows)
    titles = {}
    for r in rows:
        titles.setdefault(title_key(clean(r.get("Task"))), []).append(r["#"])
    problems += ["duplicate task title: " + ", ".join(f"#{i}" for i in ids_) for ids_ in titles.values() if len(ids_) > 1]
    for p in problems:
        print("FINDING:", p)
    print(f"{len(rows)} rows checked, {len(problems)} finding(s)")
    return 1 if problems else 0


def find_row(pid):
    path = row_path(pid)
    if not path.exists():
        sys.exit(f"no row #{pid} (expected {path.relative_to(ROOT)})")
    return path, parse_row(path.read_text(encoding="utf-8"), path.name)


def write_row(path, row):
    path.write_text(format_row(row), encoding="utf-8", newline="\n")


def validate_field(name, value):
    if name == "Status" and value not in STATUS:
        sys.exit(f"unknown status '{value}' (one of: {', '.join(STATUS)})")
    if name == "Priority" and value not in PRIORITY:
        sys.exit(f"unknown priority '{value}' (one of: {', '.join(PRIORITY)})")
    if "\n" in value:
        sys.exit(f"{name}: front matter values are one line; use a section for long text")


def cmd_set(args):
    if len(args) < 2:
        sys.exit("usage: set <id> key=value [key=value ...]   (keys: " + ", ".join(k for _, k in FIELDS[1:]) + ")")
    path, row = find_row(args[0])
    for pair in args[1:]:
        key, sep, value = pair.partition("=")
        if not sep or key not in NAME_OF or key == "id":
            sys.exit(f"bad field '{pair}' (keys: " + ", ".join(k for _, k in FIELDS[1:]) + ")")
        validate_field(NAME_OF[key], value.strip())
        row[NAME_OF[key]] = value.strip()
    write_row(path, row)
    print(f"updated {path.relative_to(ROOT)}")


def cmd_report(args):
    if len(args) < 2:
        sys.exit('usage: report <id> "text" | report <id> - (stdin) | report <id> "+text" (append a paragraph)')
    path, row = find_row(args[0])
    text = sys.stdin.read() if args[1] == "-" else args[1]
    if text.startswith("+"):
        text = (clean(row.get("Report")) + "\n\n" + text[1:].strip()).strip()
    row["Report"] = text.strip()
    write_row(path, row)
    print(f"updated {path.relative_to(ROOT)}")


def cmd_new(args):
    if len(args) < 2 or not args[0].isdigit():
        sys.exit('usage: new <id> "Task title" [key=value ...]   (id = the Jira key number)')
    path = row_path(args[0])
    if path.exists():
        sys.exit(f"{path.relative_to(ROOT)} already exists")
    row = {"#": int(args[0]), "Task": args[1], "Doer": "Claude", "Status": "Not Started", "Priority": "P1", "Track": "Docs"}
    for pair in args[2:]:
        key, sep, value = pair.partition("=")
        if not sep or key not in NAME_OF or key == "id":
            sys.exit(f"bad field '{pair}'")
        validate_field(NAME_OF[key], value.strip())
        row[NAME_OF[key]] = value.strip()
    row.update({"Evidence": "Why: ...", "Prompt": "1) ...", "Requirement": "Done when: ..."})
    write_row(path, row)
    print(f"created {path.relative_to(ROOT)}; fill Evidence, Prompt, Requirement, then run check")


def cmd_show(args):
    path, _ = find_row(args[0] if args else "")
    sys.stdout.write(path.read_text(encoding="utf-8"))


def summary_text():
    _, rows = load_rows()
    n = lambda pred: sum(1 for r in rows if pred(r))
    closed = ("Done", "Later", "Cancelled", "Obsolete")
    out = [f"Total tasks: {len(rows)}", f"Owner tasks (Doer = User): {n(lambda r: clean(r.get('Doer')) == 'User')}", "",
           "Status x priority", "| Status | P0 | P1 | P2 | P3 | Total |", "| --- | --- | --- | --- | --- | --- |"]
    for st in STATUS:
        cells = [n(lambda r: clean(r.get("Status")) == st and clean(r.get("Priority")) == p) for p in PRIORITY]
        out.append(f"| {st} | " + " | ".join(map(str, cells)) + f" | {sum(cells)} |")
    out += ["", "Area x priority (remaining = not Done/Later/Cancelled/Obsolete)", "| Area | Total | P0 | P1 | P2 | P3 | Remaining | Done |",
            "| --- | --- | --- | --- | --- | --- | --- | --- |"]
    for area in dict.fromkeys(clean(r.get("Area")) for r in rows):
        mine = [r for r in rows if clean(r.get("Area")) == area]
        pr = [sum(1 for r in mine if clean(r.get("Priority")) == p) for p in PRIORITY]
        rem = sum(1 for r in mine if clean(r.get("Status")) not in closed)
        out.append(f"| {area} | {len(mine)} | " + " | ".join(map(str, pr)) + f" | {rem} | {sum(1 for r in mine if clean(r.get('Status')) == 'Done')} |")
    out += ["", "Group (related tasks decided and planned together)", "| Group | Total | Done | Later | Remaining | Confirmed by PM |", "| --- | --- | --- | --- | --- | --- |"]
    for g in dict.fromkeys(clean(r.get("Group")) for r in rows):
        mine = [r for r in rows if clean(r.get("Group")) == g]
        done = sum(1 for r in mine if clean(r.get("Status")) == "Done")
        later = sum(1 for r in mine if clean(r.get("Status")) == "Later")
        rem = sum(1 for r in mine if clean(r.get("Status")) not in ("Done", "Later", "Cancelled", "Obsolete"))
        out.append(f"| {g} | {len(mine)} | {done} | {later} | {rem} | {sum(1 for r in mine if clean(r.get('Confirmed by PM')) == 'Yes')} |")
    open_ = lambda r: clean(r.get("Status")) not in closed
    out += ["", f"Confirmed by PM (all rows): {n(lambda r: clean(r.get('Confirmed by PM')) == 'Yes')}",
            f"P0 rows confirmed by PM: {n(lambda r: clean(r.get('Confirmed by PM')) == 'Yes' and clean(r.get('Priority')) == 'P0')}",
            f"Open P0 rows not yet confirmed by PM: {n(lambda r: clean(r.get('Confirmed by PM')) == 'No' and clean(r.get('Priority')) == 'P0' and open_(r))}",
            f"Rows on Hold: {n(lambda r: clean(r.get('Status')) == 'Hold')}",
            f"Remaining P0 tasks (not Done/Later/Cancelled/Obsolete): {n(lambda r: clean(r.get('Priority')) == 'P0' and open_(r))}",
            f"Remaining 'Sales blocker' tasks: {n(lambda r: clean(r.get('Commercial')) == 'Sales blocker' and clean(r.get('Status')) != 'Done')}",
            f"Open decisions (Decision Needed): {n(lambda r: clean(r.get('Track')) == 'Decision Needed' and clean(r.get('Status')) != 'Done')}",
            "", "Tasks per milestone", "| Milestone | Tasks |", "| --- | --- |"]
    for m in sorted({clean(r.get("Milestone")) or "-" for r in rows}):
        out.append(f"| {m} | {n(lambda r: (clean(r.get('Milestone')) or '-') == m)} |")
    return "\n".join(out)


# ---------------------------------------------------------------- tracker side (backlog #336): reconcile, pull, tracker-check, push
# Jira status -> plan status written by `pull`, and the plan statuses that already agree with a Jira status (nothing to write then).
TRACKER_TO_PLAN = {"To Do": "Not Started", "In Progress": "In Progress", "In Review": "Partial", "Done": "Done",
                   "On Hold": "Hold", "Backlog": "Later", "Won't Do": "Cancelled"}
AGREES = {"To Do": {"Not Started"}, "In Progress": {"In Progress", "Partial"}, "In Review": {"In Progress", "Partial"},
          "Done": {"Done"}, "On Hold": {"Hold"}, "Backlog": {"Later"}, "Won't Do": {"Cancelled", "Obsolete"}}
MANAGED_LABEL = re.compile(r"^(effort-|doer-|area-|plan-\d+$)")
MANAGED_FIXED = {"pm-confirmed", "partial", "later", "sales-blocker", "sprint-1-stretch"}
TRACKER_MAP = OUT_DIR / "tracker-map.csv"
ENV_NAMES = ("JIRA_BASE_URL", "JIRA_PROJECT_KEY", "JIRA_USER_EMAIL", "JIRA_API_TOKEN")


class TrackerError(Exception):
    pass


def default_transport(method, url, headers, body):
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()
    except urllib.error.URLError as e:
        raise TrackerError(f"{method} {url}: {e.reason}")


class Jira:
    """Minimal Jira Cloud REST client (standard library). `transport(method, url, headers, body) -> (status, bytes)` is replaceable in tests."""

    def __init__(self, env=None, transport=None, sleep=time.sleep):
        env = os.environ if env is None else env
        missing = [n for n in ENV_NAMES if not env.get(n)]
        if missing:
            raise TrackerError("missing environment variable(s): " + ", ".join(missing) + " (CI secrets, never files in git)")
        self.base = env["JIRA_BASE_URL"].rstrip("/")
        if not (self.base.startswith("https://") or re.match(r"http://(localhost|127\.0\.0\.1)(:|/|$)", self.base)):
            raise TrackerError("JIRA_BASE_URL must be https")
        self.project = env["JIRA_PROJECT_KEY"]
        if not re.fullmatch(r"[A-Z][A-Z0-9_]+", self.project):
            raise TrackerError("JIRA_PROJECT_KEY is not a project key")
        token = b64encode(f"{env['JIRA_USER_EMAIL']}:{env['JIRA_API_TOKEN']}".encode()).decode()
        self.headers = {"Authorization": "Basic " + token, "Accept": "application/json", "Content-Type": "application/json"}
        self.transport, self.sleep = transport or default_transport, sleep

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body).encode()
        for attempt in range(4):
            status, raw = self.transport(method, self.base + path, self.headers, data)
            if status in (429, 502, 503, 504) and attempt < 3:
                self.sleep(2 ** attempt)
                continue
            break
        if status >= 400:
            raise TrackerError(f"{method} {path}: HTTP {status} {raw[:200].decode('utf-8', 'replace')}")
        return json.loads(raw) if raw else {}

    def issues(self):
        out, token = [], None
        while True:
            body = {"jql": f'project = "{self.project}" ORDER BY key', "fields": ["summary", "status", "labels", "priority", "parent", "issuetype"], "maxResults": 100}
            if token:
                body["nextPageToken"] = token
            page = self.request("POST", "/rest/api/3/search/jql", body)
            for i in page.get("issues", []):
                f = i.get("fields", {})
                out.append({"key": i["key"], "summary": clean(f.get("summary")), "status": clean((f.get("status") or {}).get("name")),
                            "labels": list(f.get("labels") or []), "priority": clean((f.get("priority") or {}).get("name")),
                            "parent": clean((f.get("parent") or {}).get("key")), "type": clean((f.get("issuetype") or {}).get("name"))})
            token = page.get("nextPageToken")
            if page.get("isLast", True) or not token:
                return out

    def update(self, key, fields, label_ops):
        body = {"fields": fields}
        if label_ops:
            body["update"] = {"labels": label_ops}
        self.request("PUT", f"/rest/api/2/issue/{key}?notifyUsers=false", body)


def issues_from_csv(path):
    """Jira CSV export: columns 'Issue key', 'Summary', 'Status', 'Priority' and one or more 'Labels'."""
    with open(path, newline="", encoding="utf-8-sig") as fh:
        rows = list(csv.reader(fh))
    head = rows[0]
    col = lambda n: head.index(n) if n in head else None
    k, s, st, pr = col("Issue key"), col("Summary"), col("Status"), col("Priority")
    if k is None or s is None or st is None:
        raise TrackerError(f"{path}: needs the columns 'Issue key', 'Summary' and 'Status'")
    lab = [i for i, h in enumerate(head) if h in ("Labels", "Label")]
    return [{"key": r[k], "summary": r[s].strip(), "status": r[st].strip(), "priority": r[pr].strip() if pr is not None and pr < len(r) else "",
             "labels": [r[i] for i in lab if i < len(r) and r[i]]} for r in rows[1:] if len(r) > max(k, s, st) and r[k]]


def load_issues(args):
    """--csv FILE | --json FILE (offline) or the REST API (environment variables)."""
    for flag, reader in (("--csv", issues_from_csv), ("--json", lambda p: json.loads(Path(p).read_text(encoding="utf-8")))):
        if flag in args:
            i = args.index(flag)
            if i + 1 >= len(args):
                sys.exit(f"{flag} needs a file")
            return reader(args[i + 1])
    return Jira().issues()


def load_tracker_map():
    if not TRACKER_MAP.exists():
        return {}
    with TRACKER_MAP.open(newline="", encoding="utf-8") as fh:
        return {int(r["plan_id"]): r["jira_key"] for r in csv.DictReader(fh)}


def plan_id_of(issue):
    """Plan ID of an issue: its plan-N label, else the [#N] prefix of the summary."""
    for lab in issue.get("labels", []):
        m = re.fullmatch(r"plan-(\d+)", lab)
        if m:
            return int(m.group(1))
    m = re.match(r"\s*\[#(\d+)\]", issue.get("summary", ""))
    return int(m.group(1)) if m else None


def title_key(summary):
    return re.sub(r"[^a-z0-9]+", " ", re.sub(r"^\s*\[#\d+\]\s*", "", summary).casefold()).strip()


def index_issues(issues):
    by_plan = {}
    for i in issues:
        pid = plan_id_of(i)
        if pid:
            by_plan.setdefault(pid, []).append(i)
    return {i["key"]: i for i in issues}, by_plan


def issue_of(row, by_key, by_plan, tmap):
    """The issue of a row: the one with its Plan ID (preferring the key in tracker-map.csv), else the key in tracker-map.csv."""
    mine = by_plan.get(row["#"], [])
    return next((i for i in mine if i["key"] == tmap.get(row["#"])), mine[0] if mine else by_key.get(tmap.get(row["#"])))


def reconcile(rows, issues, tmap):
    """Pair rows with issues and list everything that disagrees."""
    by_key, by_plan = index_issues(issues)
    ids = {r["#"] for r in rows}
    out = {"status": [], "unknown_status": [], "no_issue": [], "no_row": [], "no_plan_id": [], "duplicates": []}
    for r in rows:
        issue = issue_of(r, by_key, by_plan, tmap)
        if issue is None:
            out["no_issue"].append(r["#"])
        elif issue["status"] not in TRACKER_TO_PLAN:
            out["unknown_status"].append((r["#"], issue["key"], issue["status"]))
        elif clean(r.get("Status")) not in AGREES[issue["status"]]:
            out["status"].append((r["#"], issue["key"], clean(r.get("Status")), issue["status"], TRACKER_TO_PLAN[issue["status"]]))
    for i in issues:
        pid = plan_id_of(i)
        if pid is None:
            out["no_plan_id"].append((i["key"], i["summary"]))
        elif pid not in ids:
            out["no_row"].append((i["key"], pid, i["summary"]))
    groups = {}
    for i in issues:
        if i["status"] != "Won't Do" and title_key(i["summary"]):          # a duplicate closed as Won't Do is already resolved
            groups.setdefault(title_key(i["summary"]), []).append(i["key"])
    out["duplicates"] = [sorted(v) for v in groups.values() if len(v) > 1]
    return out


def reconcile_lines(res):
    out = [f"{len(res['status'])} status difference(s) (Jira owns status; `pull` writes them)"]
    out += [f"  #{pid} {key}: plan '{plan}' -> '{target}' (Jira: {tracker})" for pid, key, plan, tracker, target in res["status"]]
    out += [f"{len(res['unknown_status'])} issue(s) in a Jira status the tool does not know (not pulled)"]
    out += [f"  #{pid} {key}: '{st}'" for pid, key, st in res["unknown_status"]]
    out += [f"{len(res['no_issue'])} plan row(s) without an issue"] + [f"  #{p}" for p in res["no_issue"]]
    out += [f"{len(res['no_row'])} issue(s) with a Plan ID but no plan row"] + [f"  {k}: #{p} {t}" for k, p, t in res["no_row"]]
    out += [f"{len(res['no_plan_id'])} issue(s) without a Plan ID (no plan-N label, no [#N] in the title)"] + [f"  {k}: {t}" for k, t in res["no_plan_id"]]
    out += [f"{len(res['duplicates'])} duplicate title(s)"] + ["  " + ", ".join(g) for g in res["duplicates"]]
    return out


def tracker_findings(res):
    """What fails the CI check: drift between plan and tracker other than status (status is pulled nightly)."""
    f = [f"{k} has no Plan ID (add the label plan-N or a [#N] title prefix): {t}" for k, t in res["no_plan_id"]]
    f += ["duplicate title: " + ", ".join(g) for g in res["duplicates"]]
    f += [f"{k}: Plan ID #{p} has no plan row" for k, p, _ in res["no_row"]]
    f += [f"plan row #{p} has no tracker issue" for p in res["no_issue"]]
    return f


HYGIENE_FROM = 400          # issues before this key were imported or created before the creation rules existed
HYGIENE_PREFIXES = ("area-", "doer-", "effort-")


def hygiene_findings(issues):
    """Creation rules of chapter 00 (rule 28 and 'no issue without an epic'): epic, [#N] title equal to the plan-N label, area/doer/effort labels,
    qa-pending on In Review. The parent is checked only when the source says (REST API, CSV 'Parent' column); Done, Won't Do and duplicates are skipped."""
    out = []
    for i in issues:
        m = re.fullmatch(r"[A-Z][A-Z0-9_]*-(\d+)", i["key"])
        labs = i.get("labels", [])
        if not m or int(m.group(1)) < HYGIENE_FROM or i.get("status") in ("Done", "Won't Do") or "duplicate" in labs or i.get("type") == "Epic":
            continue
        k = i["key"]
        if "parent" in i and not i["parent"]:
            out.append(f"{k} has no parent epic (every issue is created with its epic; attach it now)")
        title = re.match(r"\s*\[#(\d+)\]", i.get("summary", ""))
        label = next((int(x.group(1)) for x in map(lambda l: re.fullmatch(r"plan-(\d+)", l), labs) if x), None)
        if not title:
            out.append(f"{k} title has no [#N] Plan ID prefix: {i.get('summary', '')[:60]}")
        elif label is not None and label != int(title.group(1)):
            out.append(f"{k} title [#{title.group(1)}] and label plan-{label} disagree")
        if label is None:
            out.append(f"{k} has no plan-N label")
        out += [f"{k} has no {p}* label" for p in HYGIENE_PREFIXES if not any(l.startswith(p) for l in labs)]
        if i.get("status") == "In Review" and "qa-pending" not in labs and "qa-passed" not in labs and "qa-failed" not in labs:
            out.append(f"{k} is In Review without the label qa-pending")
    return out


def cmd_reconcile(args):
    _, rows = load_rows()
    print("\n".join(reconcile_lines(reconcile(rows, load_issues(args), load_tracker_map()))))
    return 0


def cmd_tracker_check(args):
    _, rows = load_rows()
    issues = load_issues(args)
    problems = tracker_findings(reconcile(rows, issues, load_tracker_map())) + hygiene_findings(issues)
    for p in problems:
        print("FINDING:", p)
    print(f"tracker check: {len(problems)} finding(s)")
    return 1 if problems else 0


def cmd_pull(args):
    _, rows = load_rows()
    res = reconcile(rows, load_issues(args), load_tracker_map())
    dry = "--dry-run" in args
    for pid, key, plan, tracker, target in res["status"]:
        print(f"#{pid} {key}: {plan} -> {target} (Jira: {tracker})" + (" [dry run]" if dry else ""))
        if not dry:
            path, row = find_row(pid)
            row["Status"] = target
            write_row(path, row)
    print(f"{len(res['status'])} row(s) " + ("would change" if dry else "updated") + f"; {len(res['no_issue'])} row(s) without issue, "
          f"{len(res['no_row'])} + {len(res['no_plan_id'])} issue(s) without row or Plan ID (not touched). Run `check` next: a row that became Done needs Evidence.")
    return 0


def push_actions(rows, issues, tmap):
    """Per matched issue: the fields and label changes that bring it in line with the plan (status is never pushed)."""
    by_key, by_plan = index_issues(issues)
    actions = []
    for r in rows:
        issue = issue_of(r, by_key, by_plan, tmap)
        if issue is None:
            continue
        want_labels = labels(r) + [f"plan-{r['#']}"]
        fields = {}
        if issue["summary"] != summary(r):
            fields["summary"] = summary(r)
        want_priority = PRIORITY.get(clean(r.get("Priority")), "Medium")
        if issue["priority"] != want_priority:
            fields["priority"] = {"name": want_priority}
        have = set(issue["labels"])
        ops = [{"add": x} for x in want_labels if x not in have]
        ops += [{"remove": x} for x in sorted(have) if (MANAGED_LABEL.match(x) or x in MANAGED_FIXED) and x not in want_labels]
        if fields or ops:
            actions.append((r["#"], issue["key"], fields, ops))
    return actions


def cmd_push(args):
    _, rows = load_rows()
    apply_ = "--apply" in args
    client = Jira() if apply_ else None                     # a dry run needs no credentials when --csv/--json is given
    issues = load_issues(args) if ("--csv" in args or "--json" in args) else client.issues() if client else Jira().issues()
    actions = push_actions(rows, issues, load_tracker_map())
    for pid, key, fields, ops in actions:
        print(f"#{pid} {key}: " + json.dumps({**fields, "labels": ops}, ensure_ascii=False) + ("" if apply_ else " [dry run]"))
        if apply_:
            client.update(key, fields, ops)
    print(f"{len(actions)} issue(s) " + ("updated" if apply_ else "differ; run with --apply to send (needs the JIRA_* variables)"))
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "export":
        export()
    elif cmd == "check":
        sys.exit(check())
    elif cmd == "summary":
        print(summary_text())
    elif cmd == "set":
        cmd_set(sys.argv[2:])
    elif cmd == "report":
        cmd_report(sys.argv[2:])
    elif cmd == "new":
        cmd_new(sys.argv[2:])
    elif cmd == "show":
        cmd_show(sys.argv[2:])
    elif cmd in ("reconcile", "pull", "tracker-check", "push"):
        try:
            sys.exit({"reconcile": cmd_reconcile, "pull": cmd_pull, "tracker-check": cmd_tracker_check, "push": cmd_push}[cmd](sys.argv[2:]))
        except TrackerError as e:
            sys.exit(f"tracker: {e}")
    else:
        sys.exit(__doc__)
