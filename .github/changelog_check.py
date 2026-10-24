#!/usr/bin/env python3
"""Checks CHANGELOG.md against the changelog rule (docs/development.md#changelog). CI's Changelog job runs it on every
pull request; run it locally with `python3 .github/changelog_check.py CHANGELOG.md`.

Every "## x.y.z" version (the text above the first version is free) is checked for:
  - sections: only "### For users" and "### For admins", each at most once; no "Internal" or any other section;
  - no empty section: a heading with no "- " line under it;
  - only headings and "- " lines: no loose text inside a version;
  - plain words: no code-style text in a line, meaning backticks, file names (provisioning.yml), paths (docker/x),
    command flags (-Stack, --ref), settings keys (removal.voteDays) or environment variable names (DEV_DISCORD_*);
  - no placeholders: "nothing this time", "n/a", "TBD" and the like;
  - no summary without substance: a line with "minor fixes", "various improvements" or another summary phrase must say,
    on the same line, what that changes (at least five more words);
  - versions: "## x.y.z", each once, newest first.

`--self-test` runs every rule against built-in samples and fails if one doesn't catch its sample.
"""
import os
import re
import sys

SECTIONS = ("For users", "For admins")

EXTENSIONS = "cs|csproj|props|ps1|psm1|cmd|bat|sh|py|ya?ml|json|md|toml|xml|env|txt|ts|tsx|js|css|dll|exe|sln"
CODE = [
    (re.compile(r"`"), "code formatting (backticks)"),
    (re.compile(r"\b[\w.-]+\.(?:%s)\b" % EXTENSIONS, re.IGNORECASE), "a file name"),
    (re.compile(r"(?:^|\s)(?:\.{1,2}/|~/|[\w.-]+/[\w.-]+/)|\b(?:docker|src|docs|deploy|\.github)/|\w\\\w"), "a file path"),
    (re.compile(r"(?:^|[\s(])--?[A-Za-z][\w-]*"), "a command flag"),
    (re.compile(r"\b[a-z]+\.[a-z]+[A-Z]\w*\b"), "a settings key"),
    (re.compile(r"\b[A-Z][A-Z0-9]*_[A-Z0-9_]+\b"), "an environment variable name"),
]

PLACEHOLDER_PHRASES = ["nothing this time", "nothing to report", "nothing new", "no changes", "no user-facing changes",
                       "nothing for users", "nothing for admins", "tbd", "todo", "placeholder", "lorem ipsum"]
PLACEHOLDER_LINES = {"none", "n/a", "na", "-", "nothing", "...", "etc", "see above", "same as above"}

SUMMARY_PHRASES = ["minor fixes", "minor improvements", "minor changes", "minor updates", "various improvements",
                   "various fixes", "various changes", "various updates", "bug fixes", "small fixes", "small improvements",
                   "general improvements", "performance improvements", "stability improvements", "improvements",
                   "fixes", "misc", "miscellaneous", "tweaks", "cleanup", "clean-up", "updates", "changes"]
FILLER = ["and", "&", "other", "some", "a few", "several", "plus", "more", "lots of", "many"]


def words(text):
    return re.findall(r"[A-Za-z0-9']+", text.lower())


def strip(line):
    return re.sub(r"[*_]", "", line).strip()


def check_line(text):
    """Problems with one '- ' line's text."""
    problems = []
    for pattern, what in CODE:
        match = pattern.search(text)
        if match:
            problems.append("%s (%r): name things the way the reader sees them in Discord" % (what, match.group(0).strip()))

    plain = strip(text).lower().rstrip(".!:")
    if plain in PLACEHOLDER_LINES or any(re.search(r"\b%s\b" % re.escape(p), plain) for p in PLACEHOLDER_PHRASES):
        problems.append("placeholder text: leave the section out instead")

    if any(re.search(r"\b%s\b" % re.escape(p), plain) for p in SUMMARY_PHRASES):
        rest = plain
        for phrase in SUMMARY_PHRASES + FILLER:
            rest = re.sub(r"\b%s\b" % re.escape(phrase), " ", rest)
        if len(words(rest)) < 5:
            problems.append("a summary without substance: say on the same line what it changes for the reader")
    return problems


def check(text):
    """[(line number, problem)] for a whole CHANGELOG.md."""
    problems = []
    version = None
    seen_versions = []
    section = None
    section_line = 0
    section_items = 0
    seen_sections = set()

    def close_section():
        if section is not None and section_items == 0:
            problems.append((section_line, "empty section '%s': leave the heading out when there's nothing under it" % section))

    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.rstrip()
        if line.startswith("## "):
            close_section()
            section = None
            seen_sections = set()
            match = re.fullmatch(r"## (\d+)\.(\d+)\.(\d+)", line)
            if not match:
                problems.append((number, "a version heading is '## x.y.z' and nothing else"))
                version = None
                continue
            version = tuple(int(part) for part in match.groups())
            if version in seen_versions:
                problems.append((number, "version %s appears twice" % ".".join(map(str, version))))
            elif seen_versions and version > seen_versions[-1]:
                problems.append((number, "versions go newest first: %s is above %s" % (".".join(map(str, seen_versions[-1])), line[3:])))
            seen_versions.append(version)
        elif version is None:
            continue
        elif line.startswith("### "):
            close_section()
            section = line[4:].strip()
            section_line = number
            section_items = 0
            if section not in SECTIONS:
                problems.append((number, "section '%s': only 'For users' and 'For admins'; work neither sees is left out" % section))
            elif section in seen_sections:
                problems.append((number, "section '%s' appears twice in this version" % section))
            seen_sections.add(section)
        elif line.startswith("- "):
            if section is None:
                problems.append((number, "a line outside 'For users' or 'For admins'"))
            section_items += 1
            problems.extend((number, p) for p in check_line(line[2:]))
        elif line.strip():
            problems.append((number, "only headings and '- ' lines inside a version"))
    close_section()
    return problems


BAD = """# Changelog

## 0.9.0

### For users
- **Requests:** run `/request` to ask.
- Set it in provisioning.yml.
- Edit docker/compose.yaml first.
- Start it with -Stack dev.
- Change removal.voteDays to 3.
- Set DEV_DISCORD_BOT_TOKEN.
- Nothing this time.
- Minor fixes.
- Various improvements and bug fixes.

### For admins

### Internal
- Refactor.

Loose text.

## 0.9.1

## 0.9.1
"""

GOOD = """# Changelog

Text before the first version is free.

## 0.9.0

### For users
- **Minor fixes:** request messages no longer show a Cancel button after a request is approved.
- **Long TV shows:** you can now pick any season, not just the 24 most recent.

### For admins
- **Removals:** Sonarr/Radarr titles nobody requested can be removed too, from /admin requests.
- Fixed: approving a request sometimes left the old Cancel button working, and the queue count updates after a restart.

## 0.8.0
"""


def self_test():
    found = check(BAD)
    expected = ["backticks", "a file name", "a file path", "a command flag", "a settings key", "an environment variable name",
                "placeholder", "summary without substance", "empty section", "section 'Internal'", "only headings",
                "appears twice", "newest first"]
    missing = [e for e in expected if not any(e in p for _, p in found)]
    summaries = sum("summary without substance" in p for _, p in found)
    good = check(GOOD)
    for _, p in found:
        print("  caught: " + p)
    if missing or summaries != 2 or good:
        print("SELF-TEST FAILED: missed %s; summaries caught %d of 2; good sample problems %s" % (missing, summaries, good))
        return 1
    print("Self-test passed: every rule caught its sample, and the good sample passed.")
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 2:
        print(__doc__)
        return 2
    path = argv[1]
    with open(path, encoding="utf-8") as handle:
        problems = check(handle.read())
    for number, problem in problems:
        if os.environ.get("GITHUB_ACTIONS"):
            print("::error file=%s,line=%d::%s" % (path, number, problem))
        else:
            print("%s:%d: %s" % (path, number, problem))
    if problems:
        print("%d problem(s): see the changelog rule in docs/development.md#changelog." % len(problems))
        return 1
    print("%s follows the changelog rule." % path)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
