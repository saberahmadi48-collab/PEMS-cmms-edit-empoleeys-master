#!/usr/bin/env python3
"""Jira key check for pull requests (SCRUM-349, plan #335): python3 tools/commit-key-check/check_commit_keys.py --help

A pull request passes when the Jira key SCRUM-<n> appears in the PR title, in the head branch name and in the subject of every
commit of the PR. Exempt: merge commits, bot commits and bot PRs (dependabot[bot], ...), and PRs whose head branch is
main, staging or demo (promotion merges). Standard library only, no network: the CI step passes the PR data in.
"""
import argparse, os, re, subprocess, sys

KEY = re.compile(r"(?<![A-Za-z0-9])SCRUM-\d+(?!\d)")
EXEMPT_BRANCHES = {"main", "staging", "demo"}
FIELD_SEP = "\x1f"
LOG_FORMAT = FIELD_SEP.join(["%H", "%P", "%an", "%s"])


def keys_in(text):
    return sorted(set(KEY.findall(text or "")), key=lambda k: int(k.split("-")[1]))


def is_bot(name):
    return (name or "").strip().lower().endswith("[bot]")


def parse_log(text):
    """Lines of `git log --format=LOG_FORMAT` -> list of commit dicts."""
    commits = []
    for line in text.splitlines():
        if not line.strip():
            continue
        sha, parents, author, subject = (line.split(FIELD_SEP) + ["", "", "", ""])[:4]
        commits.append({"sha": sha, "parents": parents.split(), "author": author, "subject": subject})
    return commits


def is_exempt_commit(commit):
    return len(commit["parents"]) > 1 or commit["subject"].startswith("Merge ") or is_bot(commit["author"])


def check(title, branch, commits, author=""):
    """Returns (errors, warnings); an empty errors list means the PR passes."""
    if is_bot(author) or branch in EXEMPT_BRANCHES:
        return [], []
    errors, warnings = [], []
    title_keys = keys_in(title)
    if not title_keys:
        errors.append(f"PR title has no Jira key (expected 'SCRUM-123: ...'): {title!r}")
    if not keys_in(branch):
        errors.append(f"branch name has no Jira key (expected 'feature/SCRUM-123-short-name[-hamid]'): {branch!r}")
    commit_keys = set()
    for c in commits:
        if is_exempt_commit(c):
            continue
        found = keys_in(c["subject"])
        if not found:
            errors.append(f"commit {c['sha'][:9]} has no Jira key in its subject (expected 'SCRUM-123: ...'): {c['subject']!r}")
        commit_keys.update(found)
    if title_keys:
        missing = sorted(commit_keys - set(title_keys) - set(keys_in(branch)))
        if missing:
            warnings.append("keys in commits but not in the PR title or branch (batch PRs list all keys): " + ", ".join(missing))
    return errors, warnings


def git_commits(base, head):
    out = subprocess.run(["git", "log", f"--format={LOG_FORMAT}", f"{base}..{head}"], check=True, capture_output=True, text=True).stdout
    return parse_log(out)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--title", default=os.environ.get("PR_TITLE", ""))
    p.add_argument("--branch", default=os.environ.get("PR_BRANCH", ""))
    p.add_argument("--author", default=os.environ.get("PR_AUTHOR", ""))
    p.add_argument("--base", default=os.environ.get("PR_BASE_SHA", ""))
    p.add_argument("--head", default=os.environ.get("PR_HEAD_SHA", ""))
    p.add_argument("--log-file", help="read `git log --format=<LOG_FORMAT>` output from a file instead of running git (tests, dry runs)")
    a = p.parse_args(argv)
    if a.log_file:
        with open(a.log_file, encoding="utf-8") as f:
            commits = parse_log(f.read())
    elif a.base and a.head:
        commits = git_commits(a.base, a.head)
    else:
        p.error("give --base and --head (or PR_BASE_SHA / PR_HEAD_SHA), or --log-file")
    errors, warnings = check(a.title, a.branch, commits, a.author)
    for w in warnings:
        print(f"::warning::{w}")
    for e in errors:
        print(f"::error::{e}")
    if errors:
        print("Add the Jira key (SCRUM-<n>) as AGENTS.md describes: branch feature/SCRUM-123-short-name[-hamid], subject 'SCRUM-123: ...'. "
              "Rename the branch or PR title, or reword the commits (rebase or squash) and force-push your own branch.")
        return 1
    print("Jira key check passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
