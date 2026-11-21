"""Tests of the plan tool: python3 -m unittest discover -s tools/plan-sync   (standard library only)."""
import csv, json, sys, tempfile, unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import plan_sync as ps


class PlanFilesTests(unittest.TestCase):
    def test_every_row_file_round_trips_through_parse_and_format(self):
        for path in sorted(ps.ROWS_DIR.glob("*.md")):
            text = path.read_text(encoding="utf-8")
            row = ps.parse_row(text, path.name)
            self.assertEqual(ps.format_row(row), text, path.name)

    def test_file_name_matches_id_and_ids_are_unique(self):
        _, rows = ps.load_rows()
        self.assertEqual(len({r["#"] for r in rows}), len(rows))

    def test_check_has_no_findings(self):
        self.assertEqual(ps.check(), 0)

    def test_summary_counts_every_row(self):
        _, rows = ps.load_rows()
        self.assertIn(f"Total tasks: {len(rows)}", ps.summary_text())


class EditCommandTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.old = ps.ROWS_DIR
        ps.ROWS_DIR = Path(self.tmp.name)
        # the commands print paths relative to ROOT; keep that working for a temp folder
        self.old_root = ps.ROOT
        ps.ROOT = Path(self.tmp.name)

    def tearDown(self):
        ps.ROWS_DIR, ps.ROOT = self.old, self.old_root
        self.tmp.cleanup()

    def test_new_set_report_and_show(self):
        ps.cmd_new(["900", "A test task", "area=Testing"])
        path = ps.row_path(900)
        self.assertTrue(path.exists())
        ps.cmd_set(["900", "status=In Progress", "milestone=M1"])
        ps.cmd_report(["900", "first"])
        ps.cmd_report(["900", "+second"])
        row = ps.parse_row(path.read_text(encoding="utf-8"))
        self.assertEqual((row["Status"], row["Milestone"], row["Area"]), ("In Progress", "M1", "Testing"))
        self.assertEqual(row["Report"], "first\n\nsecond")
        self.assertTrue(row["Prompt"].startswith("1)"))

    def test_set_rejects_an_unknown_status_and_a_new_duplicate(self):
        ps.cmd_new(["901", "Another"])
        with self.assertRaises(SystemExit):
            ps.cmd_set(["901", "status=Almost"])
        with self.assertRaises(SystemExit):
            ps.cmd_new(["901", "Again"])


ENV = {"JIRA_BASE_URL": "https://example.atlassian.net", "JIRA_PROJECT_KEY": "SCRUM", "JIRA_USER_EMAIL": "a@b.c", "JIRA_API_TOKEN": "s3cret-token"}


def issue(key, summary, status, labels=(), priority="High"):
    return {"key": key, "summary": summary, "status": status, "labels": list(labels), "priority": priority}


class FakeJira:
    """Replaces the HTTP transport: no network in tests. `script` answers (status, payload) in order."""
    def __init__(self, script):
        self.script, self.calls = list(script), []

    def __call__(self, method, url, headers, body):
        self.calls.append((method, url, headers, json.loads(body) if body else None))
        status, payload = self.script.pop(0)
        return status, json.dumps(payload).encode()


class TrackerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.old = (ps.ROWS_DIR, ps.ROOT, ps.TRACKER_MAP)
        ps.ROWS_DIR = ps.ROOT = Path(self.tmp.name)
        ps.TRACKER_MAP = Path(self.tmp.name) / "tracker-map.csv"
        for pid, title, st in ((1, "Alpha", "Not Started"), (2, "Beta", "Partial"), (3, "Gamma", "Done"), (4, "Delta", "Not Started")):
            ps.cmd_new([str(pid), title, f"status={st}", "priority=P0"])
        self.rows = ps.load_rows()[1]

    def tearDown(self):
        ps.ROWS_DIR, ps.ROOT, ps.TRACKER_MAP = self.old
        self.tmp.cleanup()

    def issues(self):
        return [issue("SCRUM-1", "[#1] Alpha", "In Progress", ["plan-1"]),        # differs: plan Not Started
                issue("SCRUM-2", "[#2] Beta", "In Review", ["plan-2"]),            # agrees (Partial)
                issue("SCRUM-3", "[#3] Gamma", "Done", ["plan-3"]),                # agrees
                issue("SCRUM-9", "Orphan bug", "To Do"),                           # no Plan ID
                issue("SCRUM-10", "[#99] Ghost", "To Do", ["plan-99"])]            # Plan ID without a row; row #4 has no issue

    def write_json(self, issues):
        path = Path(self.tmp.name) / "issues.json"
        path.write_text(json.dumps(issues), encoding="utf-8")
        return str(path)

    def test_reconcile_lists_every_kind_of_difference(self):
        res = ps.reconcile(self.rows, self.issues(), {})
        self.assertEqual(res["status"], [(1, "SCRUM-1", "Not Started", "In Progress", "In Progress")])
        self.assertEqual(res["no_issue"], [4])
        self.assertEqual([k for k, _, _ in res["no_row"]], ["SCRUM-10"])
        self.assertEqual([k for k, _ in res["no_plan_id"]], ["SCRUM-9"])
        self.assertEqual(res["unknown_status"], [])

    def test_plan_id_from_label_or_title_prefix_and_the_tracker_map(self):
        self.assertEqual(ps.plan_id_of(issue("K-1", "x", "To Do", ["plan-7"])), 7)
        self.assertEqual(ps.plan_id_of(issue("K-1", "[#8] x", "To Do")), 8)
        self.assertIsNone(ps.plan_id_of(issue("K-1", "x", "To Do", ["other"])))
        res = ps.reconcile(self.rows, [issue("SCRUM-44", "no id here", "Done")], {4: "SCRUM-44"})
        self.assertEqual(res["status"][0][:2], (4, "SCRUM-44"))          # found through the map

    def test_duplicate_titles_ignore_the_prefix_and_wont_do(self):
        a = [issue("SCRUM-445", "[#445] Docker restore fails", "In Review", ["plan-445"]),
             issue("SCRUM-447", "Docker restore  FAILS.", "To Do", ["plan-447"])]
        self.assertEqual(ps.reconcile([], a, {})["duplicates"], [["SCRUM-445", "SCRUM-447"]])
        a[1]["status"] = "Won't Do"
        self.assertEqual(ps.reconcile([], a, {})["duplicates"], [])

    def test_unknown_tracker_status_is_reported_not_pulled(self):
        res = ps.reconcile(self.rows, [issue("SCRUM-1", "[#1] Alpha", "Blocked", ["plan-1"])], {})
        self.assertEqual(res["unknown_status"], [(1, "SCRUM-1", "Blocked")])
        self.assertEqual(res["status"], [])

    def test_pull_writes_status_and_dry_run_does_not(self):
        path = self.write_json(self.issues())
        ps.cmd_pull(["--dry-run", "--json", path])
        self.assertEqual(ps.find_row(1)[1]["Status"], "Not Started")
        ps.cmd_pull(["--json", path])
        self.assertEqual(ps.find_row(1)[1]["Status"], "In Progress")
        self.assertEqual(ps.find_row(2)[1]["Status"], "Partial")           # agreeing rows are left alone

    def test_tracker_check_fails_on_drift_and_passes_when_clean(self):
        self.assertEqual(ps.cmd_tracker_check(["--json", self.write_json(self.issues())]), 1)
        clean_issues = [issue(f"SCRUM-{i}", f"[#{i}] T{i}", "To Do", [f"plan-{i}"]) for i in (1, 2, 3, 4)]
        self.assertEqual(ps.cmd_tracker_check(["--json", self.write_json(clean_issues)]), 0)

    def test_hygiene_flags_the_creation_rules_and_skips_old_done_and_duplicates(self):
        good = dict(issue("SCRUM-500", "[#500] Fine", "In Review", ["plan-500", "area-x", "doer-claude", "effort-S", "qa-pending"]), parent="SCRUM-5", type="Task")
        self.assertEqual(ps.hygiene_findings([good]), [])
        bad = dict(issue("SCRUM-501", "No prefix", "In Review", ["area-x"]), parent="", type="Task")
        found = "\n".join(ps.hygiene_findings([bad]))
        for text in ("no parent epic", "no [#N]", "no plan-N label", "no doer-* label", "no effort-* label", "without the label qa-pending"):
            self.assertIn(text, found)
        clash = issue("SCRUM-502", "[#7] Clash", "To Do", ["plan-8", "area-x", "doer-claude", "effort-S"])
        self.assertIn("disagree", "\n".join(ps.hygiene_findings([clash])))
        skipped = [dict(bad, key="SCRUM-399"), dict(bad, status="Done"), dict(bad, labels=["duplicate"]), dict(bad, type="Epic")]
        self.assertEqual(ps.hygiene_findings(skipped), [])
        unknown_parent = issue("SCRUM-503", "[#503] X", "To Do", ["plan-503", "area-x", "doer-claude", "effort-S"])   # source without parent data (CSV): not judged
        self.assertEqual(ps.hygiene_findings([unknown_parent]), [])

    def test_tracker_check_fails_on_a_hygiene_finding(self):
        clean_issues = [issue(f"SCRUM-{i}", f"[#{i}] T{i}", "To Do", [f"plan-{i}"]) for i in (1, 2, 3, 4)]
        clean_issues.append(dict(issue("SCRUM-600", "[#600] T", "To Do", ["plan-600"]), parent="SCRUM-5", type="Task"))
        ps.cmd_new(["600", "T"])
        self.assertEqual(ps.cmd_tracker_check(["--json", self.write_json(clean_issues)]), 1)

    def test_jira_csv_export_is_read_with_repeated_labels_columns(self):
        path = Path(self.tmp.name) / "export.csv"
        with path.open("w", newline="", encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(["Issue key", "Summary", "Status", "Priority", "Labels", "Labels"])
            w.writerow(["SCRUM-1", "[#1] Alpha", "In Progress", "Highest", "plan-1", "effort-M"])
        self.assertEqual(ps.issues_from_csv(path), [issue("SCRUM-1", "[#1] Alpha", "In Progress", ["plan-1", "effort-M"], "Highest")])

    def test_push_sends_only_differences_and_never_touches_foreign_labels(self):
        row = ps.find_row(1)[1]
        have = issue("SCRUM-1", "old title", "To Do", ["plan-1", "qa-pending", "effort-XL"], priority="Low")
        (pid, key, fields, ops), = ps.push_actions([row], [have], {})
        self.assertEqual((pid, key), (1, "SCRUM-1"))
        self.assertEqual(fields, {"summary": "[#1] Alpha", "priority": {"name": "Highest"}})
        self.assertIn({"remove": "effort-XL"}, ops)
        self.assertNotIn({"remove": "qa-pending"}, ops)
        synced = issue("SCRUM-1", "[#1] Alpha", "To Do", ps.labels(row) + ["plan-1", "qa-pending"], priority="Highest")
        self.assertEqual(ps.push_actions([row], [synced], {}), [])

    def test_client_search_pages_and_update_use_the_fake_transport(self):
        def page(keys, token):
            fields = {"summary": "s", "status": {"name": "To Do"}, "labels": [], "priority": {"name": "High"}}
            body = {"issues": [{"key": k, "fields": fields} for k in keys], "isLast": token is None}
            if token:
                body["nextPageToken"] = token
            return 200, body
        fake = FakeJira([page(["SCRUM-1"], "t2"), page(["SCRUM-2"], None), (204, {})])
        jira = ps.Jira(ENV, transport=fake)
        self.assertEqual([i["key"] for i in jira.issues()], ["SCRUM-1", "SCRUM-2"])
        self.assertEqual(fake.calls[1][3]["nextPageToken"], "t2")
        self.assertTrue(fake.calls[0][2]["Authorization"].startswith("Basic "))
        jira.update("SCRUM-1", {"summary": "x"}, [{"add": "plan-1"}])
        self.assertEqual(fake.calls[2][0:2], ("PUT", "https://example.atlassian.net/rest/api/2/issue/SCRUM-1?notifyUsers=false"))

    def test_retry_on_429_and_error_message_never_contains_the_token(self):
        fake = FakeJira([(429, {}), (200, {"issues": [], "isLast": True})])
        self.assertEqual(ps.Jira(ENV, transport=fake, sleep=lambda s: None).issues(), [])
        with self.assertRaises(ps.TrackerError) as cm:
            ps.Jira(ENV, transport=FakeJira([(401, {"message": "bad credentials"})])).issues()
        self.assertIn("401", str(cm.exception))
        self.assertNotIn("s3cret-token", str(cm.exception))

    def test_configuration_errors_name_variables_and_need_https(self):
        with self.assertRaises(ps.TrackerError) as cm:
            ps.Jira({})
        self.assertIn("JIRA_API_TOKEN", str(cm.exception))
        with self.assertRaises(ps.TrackerError):
            ps.Jira({**ENV, "JIRA_BASE_URL": "http://example.atlassian.net"})
        with self.assertRaises(ps.TrackerError):
            ps.Jira({**ENV, "JIRA_PROJECT_KEY": 'X" OR 1=1'})

    def test_check_flags_a_duplicate_row_title(self):
        ps.cmd_new(["5", "alpha"])
        self.assertEqual(ps.check(), 1)


if __name__ == "__main__":
    unittest.main()
