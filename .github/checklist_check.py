#!/usr/bin/env python3
"""Checks QA-CHECKLIST.md against CHANGELOG.md (docs/development.md#qa-checklist). CI's Changelog job runs it on every
pull request; run it locally with `python3 .github/checklist_check.py QA-CHECKLIST.md CHANGELOG.md`.

The QA checklist is what admins on the QA server test, in Discord, before a version goes to staging. Every pull request
adds its version's items, the way it adds its changelog entry. Checked:
  - versions: every changelog version from 0.4.3 on has a "## x.y.z (YYYY-MM-DD)" section here, with the same date, in
    the same order, and there are no others;
  - a version with nothing anyone can test by hand has only its heading (the same escape as a heading-only changelog
    entry); otherwise only "- " item lines;
  - each item: "- **Name:** what to do. You should see what happens.", with a name unique in its version and the
    words "You should see" saying what a pass looks like;
  - plain words, as in the changelog: no backticks, file names, paths, command flags, settings keys or environment
    variable names, since admins read these in Discord.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from changelog_check import CODE  # noqa: E402

FIRST = (0, 4, 3)
VERSION = re.compile(r"^## (\d+)\.(\d+)\.(\d+) \((\d{4}-\d{2}-\d{2})\)$")
ITEM = re.compile(r"^- \*\*([^*:]+):\*\* (.+)$")


def versions(text):
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        match = VERSION.match(line.strip())
        if match:
            found.append((tuple(int(match.group(i)) for i in (1, 2, 3)), match.group(4), number))
    return found


def check(checklist, changelog):
    problems = []
    wanted = [(v, d) for v, d, _ in versions(changelog) if v >= FIRST]
    have = versions(checklist)
    if [(v, d) for v, d, _ in have] != wanted:
        missing = [v for v, _ in wanted if v not in [h[0] for h in have]]
        extra = [v for v, _, _ in have if v not in [w[0] for w in wanted]]
        problems.append((1, "versions don't match the changelog from 0.4.3 on (same versions, dates and order): missing %s, extra %s"
                         % (", ".join("%d.%d.%d" % v for v in missing) or "none", ", ".join("%d.%d.%d" % v for v in extra) or "none")))

    current = None
    names = set()
    for number, line in enumerate(checklist.splitlines(), 1):
        text = line.strip()
        if VERSION.match(text):
            current = text
            names = set()
            continue
        if current is None or not text:
            continue
        if text.startswith("#"):
            problems.append((number, "only version headings: '%s'" % text))
            continue
        item = ITEM.match(text)
        if not item:
            problems.append((number, "only '- **Name:** what to do. You should see ...' lines inside a version"))
            continue
        name, body = item.group(1).strip(), item.group(2)
        if name.lower() in names:
            problems.append((number, "item '%s' appears twice in this version" % name))
        names.add(name.lower())
        if "you should see" not in body.lower():
            problems.append((number, "item '%s' must say 'You should see' what a pass looks like" % name))
        for pattern, what in CODE:
            if pattern.search(text):
                problems.append((number, "plain words only: '%s' has %s" % (name, what)))
    return problems


BAD_CHANGELOG = """# Changelog

## 0.4.4 (2026-10-25)

## 0.4.3 (2026-10-25)
"""

BAD = """# QA checklist

## 0.4.4 (2026-10-25)
- **Logo:** look at the logo.
- **Logo:** open `provisioning.yml`. You should see it.
Some loose text.
### Section
"""

GOOD = """# QA checklist

Intro text is free.

## 0.4.4 (2026-10-25)
- **Logo:** open the web app and look at the header. You should see the Dev logo.

## 0.4.3 (2026-10-25)
"""


def self_test():
    found = check(BAD, BAD_CHANGELOG)
    expected = ["versions don't match", "must say 'You should see'", "appears twice", "backticks", "a file name",
                "only '- **Name:**", "only version headings"]
    missing = [e for e in expected if not any(e in p for _, p in found)]
    good = check(GOOD, BAD_CHANGELOG)
    for _, p in found:
        print("  caught: " + p)
    if missing or good:
        print("SELF-TEST FAILED: missed %s; good sample problems %s" % (missing, good))
        return 1
    print("Self-test passed: every rule caught its sample, and the good sample passed.")
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 3:
        print(__doc__)
        return 2
    with open(argv[1], encoding="utf-8") as handle:
        checklist = handle.read()
    with open(argv[2], encoding="utf-8") as handle:
        changelog = handle.read()
    problems = check(checklist, changelog)
    for number, problem in problems:
        if os.environ.get("GITHUB_ACTIONS"):
            print("::error file=%s,line=%d::%s" % (argv[1], number, problem))
        else:
            print("%s:%d: %s" % (argv[1], number, problem))
    if problems:
        print("%d problem(s): see the QA checklist rule in docs/development.md#qa-checklist." % len(problems))
        return 1
    print("%s matches the changelog and follows the QA checklist rule." % argv[1])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
