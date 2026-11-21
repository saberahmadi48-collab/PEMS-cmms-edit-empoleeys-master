"""Tests of the Jira key check: python3 -m unittest discover -s tools/commit-key-check   (standard library only, offline)."""
import sys, tempfile, unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_commit_keys as ck

S = ck.FIELD_SEP


def commit(subject, author="Hamid", parents=("p1",), sha="a1b2c3d4e5f6"):
    return {"sha": sha, "parents": list(parents), "author": author, "subject": subject}


class KeyTests(unittest.TestCase):
    def test_keys_are_found_only_as_whole_scrum_keys(self):
        self.assertEqual(ck.keys_in("SCRUM-349 SCRUM-18: x"), ["SCRUM-18", "SCRUM-349"])
        self.assertEqual(ck.keys_in("feature/SCRUM-349-key-ci-check-hamid"), ["SCRUM-349"])
        self.assertEqual(ck.keys_in("NOSCRUM-5"), [])
        self.assertEqual(ck.keys_in("scrum-5 PEMS-7 SCRUM-"), [])


class CheckTests(unittest.TestCase):
    def errors(self, title="SCRUM-349: x (hamid)", branch="feature/SCRUM-349-x-hamid", commits=None, author="atapems-tech"):
        return ck.check(title, branch, commits if commits is not None else [commit("SCRUM-349: x (hamid)")], author)[0]

    def test_a_conforming_pr_passes(self):
        self.assertEqual(self.errors(), [])

    def test_missing_key_in_title_branch_or_commit_fails_each_on_its_own(self):
        self.assertEqual(len(self.errors(title="fix things")), 1)
        self.assertEqual(len(self.errors(branch="feature/fix-things")), 1)
        errors = self.errors(commits=[commit("SCRUM-349: a"), commit("wip", sha="deadbeef0000")])
        self.assertEqual(len(errors), 1)
        self.assertIn("deadbeef0", errors[0])

    def test_all_three_missing_gives_three_errors(self):
        self.assertEqual(len(self.errors(title="x", branch="y", commits=[commit("z")])), 3)

    def test_merge_commits_are_exempt(self):
        self.assertEqual(self.errors(commits=[commit("SCRUM-349: a"), commit("Merge branch 'main' into feature/x", parents=("p1", "p2")),
                                              commit("Merge remote-tracking branch 'origin/main'")]), [])

    def test_bot_commits_and_bot_prs_are_exempt(self):
        self.assertEqual(self.errors(commits=[commit("SCRUM-349: a"), commit("Bump x from 1 to 2", author="dependabot[bot]")]), [])
        self.assertEqual(self.errors(title="Bump x", branch="dependabot/nuget/x-2", commits=[commit("Bump x")], author="dependabot[bot]"), [])

    def test_promotion_branches_are_exempt(self):
        for branch in ("main", "staging", "demo"):
            self.assertEqual(self.errors(title="Promote", branch=branch, commits=[commit("anything")]), [], branch)

    def test_batch_keys_missing_from_title_only_warn(self):
        errors, warnings = ck.check("SCRUM-18: a", "feature/SCRUM-18-a", [commit("SCRUM-18: a"), commit("SCRUM-349: b")])
        self.assertEqual(errors, [])
        self.assertEqual(len(warnings), 1)
        self.assertIn("SCRUM-349", warnings[0])
        _, warnings = ck.check("SCRUM-18 SCRUM-349: a", "feature/SCRUM-18-a", [commit("SCRUM-18: a"), commit("SCRUM-349: b")])
        self.assertEqual(warnings, [])


class LogAndMainTests(unittest.TestCase):
    def log(self, *lines):
        return "\n".join(S.join(l) for l in lines) + "\n"

    def test_parse_log(self):
        commits = ck.parse_log(self.log(("h1", "p1", "Hamid", "SCRUM-1: a"), ("h2", "p1 p2", "Hamid", "Merge x")) + "\n")
        self.assertEqual([c["subject"] for c in commits], ["SCRUM-1: a", "Merge x"])
        self.assertEqual(commits[1]["parents"], ["p1", "p2"])
        self.assertTrue(ck.is_exempt_commit(commits[1]))

    def test_main_exit_codes_with_a_log_file(self):
        with tempfile.TemporaryDirectory() as d:
            good, bad = Path(d, "good.log"), Path(d, "bad.log")
            good.write_text(self.log(("h1", "p1", "Hamid", "SCRUM-349: a")), encoding="utf-8")
            bad.write_text(self.log(("h1", "p1", "Hamid", "no key")), encoding="utf-8")
            args = ["--title", "SCRUM-349: a", "--branch", "feature/SCRUM-349-a-hamid"]
            self.assertEqual(ck.main(args + ["--log-file", str(good)]), 0)
            self.assertEqual(ck.main(args + ["--log-file", str(bad)]), 1)


if __name__ == "__main__":
    unittest.main()
