#!/usr/bin/env python3
"""Requestarr's build numbers (docs/development.md#versions). This is the only thing that takes a number.

A version is MAJOR.MINOR.BUILD, with BUILD a running four-digit number: 0.7.0004. A number is taken only when a build
leaves for testers, at the promotion to staging (or a fix into staging), and recorded as a git tag on the commit that
was built (v0.7.0004). A number is never reused: a bad build keeps its number and the fix takes the next one.
  - BUILD counts every build sent; it goes back to 0000 only when MAJOR rolls up.
  - MINOR rolls up once the newest build has shipped (production, "ship it", is main): 0.7.0004 shipped, next 0.8.0005.
  - MAJOR: 1.0 on the owner's word (the version 1 scope), then when admins must act on an upgrade (a "**Before you
    upgrade:**" line under For admins). MINOR goes to 0 and BUILD starts again: 1.0.0001.
Dev and QA builds take no number: they're labelled with the next one and their commit, 0.7.0004-dev.4f2a9c1.
Versions up to 0.6.0 kept their x.y.z numbers, one per pull request; three of those builds went to testers (below).

CHANGELOG.md and QA-CHECKLIST.md: pull requests add their lines under "## Unreleased"; the staging promotion turns
it into "## 0.7.0004 (YYYY-MM-DD)". Numbered sections never change after that.

  python3 .github/build_numbers.py next [--major] [--changelog <file>] [--shipped <x.y.zzzz>]
        the number the next build sent to testers takes (from 1.0, a major when that changelog's Unreleased has a
        "Before you upgrade" line for admins; --shipped: as if that build had shipped)
  python3 .github/build_numbers.py image <event> <branch> <commit> [<test tag>]
        what the Image workflow publishes, from CHANGELOG.md here: key=value lines (mode build or retag, tags, ...)
  python3 .github/build_numbers.py label <dev|qa|test> <commit>   a build that isn't sent: 0.7.0004-dev.4f2a9c1
  python3 .github/build_numbers.py release <dir> <number> <YYYY-MM-DD> [--security]
        turns Unreleased into that number in <dir>/CHANGELOG.md and <dir>/QA-CHECKLIST.md (--security: adds the line
        telling admins a security fix is in this build)
  python3 .github/build_numbers.py merge <ours dir> <theirs dir> <out dir>
        a merge-back of both files: every numbered section of both, highest first; Unreleased keeps the lines of both
        that no numbered section has yet
  python3 .github/build_numbers.py check <change|release|staging-fix|merge-back> <base dir> <head dir> [<upper dir>]
        [--number <x.y.zzzz>] [--no-changelog]   a pull request's two files against the rules; exit 1 with the reasons
  python3 .github/build_numbers.py --self-test

next reads the tags with `git ls-remote --tags origin` (no login) and which builds shipped from origin/main.
"""
import os
import re
import subprocess
import sys

UNRELEASED = "Unreleased"
HEADING = re.compile(r"^## (?:(?P<unreleased>Unreleased)|(?P<version>\d+\.\d+\.\d+)(?: .*)?)$")
TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d{4,})$")
FILES = ("CHANGELOG.md", "QA-CHECKLIST.md")

# Builds sent to testers before numbers were kept: the three staging image publishes of October 24, 2026 (0.3.1 at
# 00239c8, 0.3.3 at bd92a1e, 0.3.4 at 1bc61b8). The counter starts after them, on the first minor not already used.
EARLIER_BUILDS = 3
FIRST_NUMBERED = (0, 7)

SECTION_ORDER = ("", "For users", "For admins")
ADMINS = "For admins"
UPGRADE_LEAD = "- **Before you upgrade:**"
SECURITY_LINE = ("- **Security update:** parts Requestarr is built from were updated to fix known security problems, "
                 "so update when you can.")


def parse_version(value):
    match = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", value.strip())
    if not match:
        raise ValueError("not a version: %r" % value)
    return tuple(int(x) for x in match.groups())


def text(version):
    """How people see a version: 0.7.0004 from 0.7 on, the old x.y.z before."""
    return ("%d.%d.%04d" if version[:2] >= FIRST_NUMBERED else "%d.%d.%d") % version


# ---- the counter ------------------------------------------------------------------------------------------------

def built(tag_names):
    """The numbers taken so far, from tag names (v0.7.0004)."""
    return {tuple(int(x) for x in m.groups()) for m in map(TAG.match, tag_names) if m}


def next_number(taken, shipped, major=False):
    """The next build's number: one more than the newest, a minor up once the newest has shipped."""
    newest = max(taken) if taken else FIRST_NUMBERED + (EARLIER_BUILDS,)
    if major:
        return (newest[0] + 1, 0, 1)
    return (newest[0], newest[1] + (1 if newest in shipped else 0), newest[2] + 1)


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8")


def remote_tags():
    """{tag name: commit} from origin; an annotated tag is read by the commit it points to."""
    tags = {}
    for row in git("ls-remote", "--tags", "origin").stdout.splitlines():
        sha, _, ref = row.partition("\t")
        name = ref[len("refs/tags/"):]
        peeled = name.endswith("^{}")
        name = name[:-3] if peeled else name
        if peeled or name not in tags:
            tags[name] = sha
    return tags


def shipped_builds(tags):
    """The numbered builds production (origin/main) has."""
    return {v for name, sha in tags.items() for v in built([name])
            if git("merge-base", "--is-ancestor", sha, "origin/main").returncode == 0}


def next_from_git(major=False, shipping=None):
    """shipping: a build about to ship, counted as shipped (what ship-it's dry run shows)."""
    tags = remote_tags()
    shipping = {shipping} if shipping else set()
    return next_number(built(tags) | shipping, shipped_builds(tags) | shipping, major)


# ---- published images ---------------------------------------------------------------------------------------------

IMAGE = "ghcr.io/requestarr/requestarr"


def top_number(changelog):
    """The newest numbered version of a changelog, and whether Unreleased is above it."""
    _, unreleased, sections = parse(changelog)
    return (max(sections) if sections else None), unreleased is not None


def image_plan(event, branch, sha, changelog, tags, upcoming, test_tag=""):
    """What the Image workflow does: {"mode": "build" or "retag", "number", "label", "tags", "source", "record"}, or
    {"error": why}. Staging builds the image for the number on top of its changelog (and records the tag); main never
    builds: it adds the release tags to the image staging published from the same commit."""
    top, pending = top_number(changelog)
    tagged = {v: s for name, s in tags.items() for v in built([name])}
    if event == "push" and branch == "staging":
        if pending or top is None:
            return {"error": "staging's changelog has Unreleased on top: a build sent to testers has a number "
                             "(./release.sh promote-staging, or a fix into staging with the next number)"}
        if tagged.get(top) not in (None, sha):
            return {"error": "%s was already published from another commit: a number is never reused" % text(top)}
        if top not in tagged and top != upcoming:
            return {"error": "staging's top version is %s, but the next build takes %s" % (text(top), text(upcoming))}
        number = text(top)
        return {"mode": "build", "number": number, "label": number, "source": "",
                "tags": "%s:staging,%s:%s" % (IMAGE, IMAGE, number), "record": "" if top in tagged else "v" + number}
    if event == "push" and branch == "main":
        if top is None or tagged.get(top) != sha:
            return {"error": "main is only ever a build staging published (v<number> on this commit): ./release.sh ship-it"}
        number = text(top)
        minor = "%d.%d" % top[:2]
        return {"mode": "retag", "number": number, "label": number, "source": "%s:%s" % (IMAGE, number),
                "tags": "%s:%s,%s:latest" % (IMAGE, minor, IMAGE), "record": ""}
    if event == "workflow_dispatch" and re.fullmatch(r"test-[a-z0-9.-]+", test_tag or ""):
        return {"mode": "build", "number": text(upcoming), "label": "%s-test.%s" % (text(upcoming), sha[:7]),
                "source": "", "tags": "%s:%s" % (IMAGE, test_tag), "record": ""}
    if event == "workflow_dispatch":
        return {"error": "a test tag starts with 'test-' and has only lowercase letters, digits, dots and dashes"}
    return {"error": "images are published from staging and main only (or a test tag by hand)"}


# ---- the two files ----------------------------------------------------------------------------------------------

def parse(document):
    """(text before the first version, Unreleased as {subheading: lines} or None, {version: section text})."""
    document = document.replace("\r\n", "\n")
    preamble, unreleased, sections = [], None, {}
    current = None
    for row in document.split("\n"):
        heading = HEADING.match(row)
        if heading and heading.group("unreleased"):
            unreleased, current = {}, None
        elif heading:
            current = parse_version(heading.group("version"))
            sections[current] = row + "\n"
        elif current is not None:
            sections[current] += row + "\n"
        elif unreleased is not None:
            add_unreleased_row(unreleased, row)
        else:
            preamble.append(row)
    sections = {v: s.rstrip("\n") + "\n\n" for v, s in sections.items()}
    return "\n".join(preamble).rstrip("\n") + "\n\n", unreleased, sections


def add_unreleased_row(unreleased, row):
    if row.startswith("### "):
        unreleased.setdefault(row[4:].strip(), [])
    elif row.strip():
        last = list(unreleased)[-1] if unreleased else ""
        unreleased.setdefault(last, []).append(row.rstrip())


def ordered(unreleased):
    rank = {name: i for i, name in enumerate(SECTION_ORDER)}
    return sorted(((sub, rows) for sub, rows in unreleased.items() if rows), key=lambda item: rank.get(item[0], len(rank)))


def render_section(heading, unreleased):
    """A version's section: "## heading", then each part with its "### " subheading (the checklist has none)."""
    parts = "".join(("\n### %s\n" % sub if sub else "") + "".join(row + "\n" for row in rows) for sub, rows in ordered(unreleased or {}))
    return "## %s\n%s\n" % (heading, parts if parts else "")


def render(preamble, unreleased, sections):
    top = render_section(UNRELEASED, unreleased) if unreleased and ordered(unreleased) else ""
    body = top + "".join(sections[v] for v in sorted(sections, reverse=True))
    return (preamble + body).rstrip("\n") + "\n"


def lines_of(unreleased):
    return {row for rows in (unreleased or {}).values() for row in rows}


def released_lines(sections):
    return {row.rstrip() for section in sections.values() for row in section.split("\n") if row.startswith("- ")}


def release(document, number, day, extra_admin_line=None):
    """Unreleased becomes the numbered section (heading only when nothing is under it)."""
    preamble, unreleased, sections = parse(document)
    if number in sections:
        raise ValueError("%s is already in this file" % text(number))
    unreleased = {sub: list(rows) for sub, rows in (unreleased or {}).items()}
    if extra_admin_line and extra_admin_line not in unreleased.get(ADMINS, []):
        unreleased.setdefault(ADMINS, []).append(extra_admin_line)
    sections[number] = render_section("%s (%s)" % (text(number), day), unreleased)
    return render(preamble, None, sections)


def merge(ours, theirs):
    """Every numbered section of both, highest first (one both have must read the same); Unreleased keeps the lines of
    both, ours first, that no numbered section has yet."""
    preamble, mine, own = parse(ours)
    _, other, others = parse(theirs)
    for version in own.keys() & others.keys():
        if own[version].rstrip() != others[version].rstrip():
            raise ValueError("%s reads differently on the two branches: merge it by hand" % text(version))
    sections = {**others, **own}
    done = released_lines(sections)
    unreleased = {}
    for side in (mine or {}, other or {}):
        for sub, rows in side.items():
            kept = unreleased.setdefault(sub, [])
            kept.extend(row for row in rows if row not in done and row not in kept)
    return render(preamble, unreleased, sections)


def upgrade_needs_admins(document):
    _, unreleased, _ = parse(document)
    return any(row.startswith(UPGRADE_LEAD) for row in (unreleased or {}).get(ADMINS, []))


# ---- a pull request's rules ---------------------------------------------------------------------------------------

def check_change(base, head, no_changelog):
    """Into dev, or a fix into qa: numbered sections untouched; the changelog gains Unreleased lines, or the pull request
    says it has none."""
    problems = []
    for name in FILES:
        if parse(base[name])[2] != parse(head[name])[2]:
            problems.append("%s: a numbered version changed; add lines under '## Unreleased' instead" % name)
    added = lines_of(parse(head["CHANGELOG.md"])[1]) - lines_of(parse(base["CHANGELOG.md"])[1])
    if no_changelog and added:
        problems.append("the description says 'No changelog:', but CHANGELOG.md adds lines under Unreleased")
    elif not no_changelog and not added:
        problems.append("add what changed for users or admins under '## Unreleased' in CHANGELOG.md, or, when nothing "
                        "changes for either, a line in the pull request's description: 'No changelog: <why>'")
    return problems


def check_numbered(base, head, number, no_changelog, released):
    """A release (Unreleased renamed) or a fix into staging (a new section): exactly one new numbered section, the next
    number, and no Unreleased left."""
    problems = []
    for name in FILES:
        _, unreleased, sections = parse(head[name])
        old = parse(base[name])[2]
        if unreleased is not None:
            problems.append("%s: a build sent to testers has no Unreleased section" % name)
        if set(sections) - set(old) != {number} or any(sections.get(v) != s for v, s in old.items()):
            problems.append("%s: the only change is a new section for the next build, %s" % (name, text(number)))
    if released:
        problems += check_release_body(base, head, number)
    elif not no_changelog and not released_lines({number: parse(head["CHANGELOG.md"])[2].get(number, "")}):
        problems.append("a fix into staging says what changed in its section, or its description says 'No changelog: <why>'")
    return problems


def check_release_body(base, head, number):
    """A release keeps Unreleased word for word (the security line may be added)."""
    problems = []
    for name in FILES:
        sections = parse(head[name])[2]
        if number not in sections:
            continue
        day = re.match(r"## \S+ \((\d{4}-\d{2}-\d{2})\)", sections[number])
        day = day.group(1) if day else ""
        if head[name].replace("\r\n", "\n") not in (release(base[name], number, day), release(base[name], number, day, SECURITY_LINE)):
            problems.append("%s: the new section is Unreleased as it was, renamed %s (YYYY-MM-DD)" % (name, text(number)))
    return problems


def check_merge_back(base, upper, head):
    """Nothing lost: every numbered section of both branches, as it reads there; Unreleased only lines from either."""
    problems = []
    for name in FILES:
        _, unreleased, sections = parse(head[name])
        for side in (base, upper):
            for version, section in parse(side[name])[2].items():
                if sections.get(version) != section:
                    problems.append("%s: %s is missing or changed; a merge-back keeps every version" % (name, text(version)))
        known = lines_of(parse(base[name])[1]) | lines_of(parse(upper[name])[1])
        if lines_of(unreleased) - known or lines_of(unreleased) & released_lines(sections):
            problems.append("%s: Unreleased keeps only the lines of both branches that no version has yet" % name)
    return sorted(set(problems))


def check(kind, base, head, upper=None, number=None, no_changelog=False):
    if kind == "change":
        return check_change(base, head, no_changelog)
    if kind in ("release", "staging-fix"):
        return check_numbered(base, head, number, no_changelog, kind == "release")
    if kind == "merge-back":
        return check_merge_back(base, upper, head)
    raise ValueError("unknown kind %r" % kind)


# ---- self-test --------------------------------------------------------------------------------------------------

def self_test():
    log = lambda body: "# Changelog\n\nIntro.\n\n" + body
    ticks = lambda body: "# QA checklist\n\nIntro.\n\n" + body
    old = "## 0.6.0 (2026-11-13)\n\n### For users\n- **Cards:** clearer.\n\n## 0.5.6 (2026-11-13)\n"
    old_list = "## 0.6.0 (2026-11-13)\n- **Cards:** look. You should see them.\n\n## 0.5.6 (2026-11-13)\n"
    base = {"CHANGELOG.md": log(old), "QA-CHECKLIST.md": ticks(old_list)}
    pending = "## Unreleased\n\n### For users\n- **Numbers:** builds have one.\n\n"
    change = {"CHANGELOG.md": log(pending + old), "QA-CHECKLIST.md": ticks("## Unreleased\n- **Numbers:** look. You should see one.\n\n" + old_list)}
    first = (0, 7, 4)
    released = {name: release(change[name], first, "2026-11-20") for name in FILES}
    staged_fix = {"CHANGELOG.md": released["CHANGELOG.md"].replace("## 0.7.0004", "## 0.7.0005 (2026-11-21)\n\n### For admins\n- **Fix:** it works.\n\n## 0.7.0004"),
                  "QA-CHECKLIST.md": released["QA-CHECKLIST.md"].replace("## 0.7.0004", "## 0.7.0005 (2026-11-21)\n\n## 0.7.0004")}
    newer_dev = log("## Unreleased\n\n### For users\n- **Numbers:** builds have one.\n- **Later:** after QA.\n\n" + old)
    back = merge(newer_dev, released["CHANGELOG.md"])
    tags = ["v0.3.1", "v0.7.0004", "v0.7.0005", "test-org-move"]
    cases = [
        (next_number(set(), set()) == (0, 7, 4), "the first build under build numbers is 0.7.0004 (three were sent before)"),
        (next_number(built(tags), set()) == (0, 7, 6), "every build sent takes the next number"),
        (next_number(built(tags), {(0, 7, 5)}) == (0, 8, 6), "once the newest build ships, the next one is a minor up"),
        (next_number(built(tags), {(0, 7, 4)}) == (0, 7, 6), "an older build shipping doesn't move the minor again"),
        (next_number(built(tags), set(), major=True) == (1, 0, 1), "a major: minor to 0, the count starts again"),
        (next_number({(1, 0, 1)}, set()) == (1, 0, 2), "after a major the count goes on from there"),
        (text((0, 7, 4)) == "0.7.0004" and text((0, 6, 0)) == "0.6.0" and text((0, 7, 12345)) == "0.7.12345",
         "people see four digits from 0.7 on, the old numbers as they were"),
        (check("change", base, change) == [], "a normal pull request: lines under Unreleased"),
        (check("change", base, base, no_changelog=True) == [], "a pull request nobody would notice says so instead"),
        (bool(check("change", base, base)), "no lines and nothing said fails"),
        (bool(check("change", base, change, no_changelog=True)), "'No changelog' with lines fails"),
        (bool(check("change", base, {**change, "CHANGELOG.md": change["CHANGELOG.md"].replace("clearer", "better")})),
         "changing a numbered version fails"),
        (check("change", change, {**change, "CHANGELOG.md": newer_dev}) == [], "a fix into QA adds to QA's Unreleased"),
        ("## 0.7.0004 (2026-11-20)\n\n### For users\n- **Numbers:** builds have one.\n\n## 0.6.0" in released["CHANGELOG.md"]
         and "## Unreleased" not in released["CHANGELOG.md"], "the staging promotion turns Unreleased into the number"),
        ("## 0.7.0004 (2026-11-20)\n- **Numbers:**" in released["QA-CHECKLIST.md"], "the checklist's Unreleased too"),
        ("## 0.7.0004 (2026-11-20)\n\n## 0.6.0" in release(base["CHANGELOG.md"], first, "2026-11-20"),
         "a build with nothing new is its heading only"),
        (SECURITY_LINE in release(change["CHANGELOG.md"], first, "d", SECURITY_LINE).split("## 0.6.0")[0],
         "a security fix in the build tells admins"),
        (check("release", change, released, number=first) == [], "the release pull request passes"),
        (bool(check("release", change, released, number=(0, 7, 5))), "a release with the wrong number fails"),
        (bool(check("release", change, {**released, "CHANGELOG.md": released["CHANGELOG.md"].replace("builds have one", "x")},
                    number=first)), "a release that rewrites the lines fails"),
        (check("staging-fix", released, staged_fix, number=(0, 7, 5)) == [], "a fix into staging takes the next number"),
        (bool(check("staging-fix", released, staged_fix, number=(0, 7, 6))), "a staging fix with another number fails"),
        (bool(check("staging-fix", released, change, number=(0, 7, 5))), "a staging fix under Unreleased fails"),
        ("## Unreleased\n\n### For users\n- **Later:** after QA.\n\n## 0.7.0004" in back, "a merge-back keeps only new lines in Unreleased"),
        (check("merge-back", {**base, "CHANGELOG.md": newer_dev}, {**released, "CHANGELOG.md": back}, released) == [],
         "the merge-back passes its check"),
        (bool(check("merge-back", base, base, released)), "a merge-back that loses a version fails"),
        (merge(released["CHANGELOG.md"], staged_fix["CHANGELOG.md"]) == staged_fix["CHANGELOG.md"], "a staging fix merges down whole"),
        (upgrade_needs_admins(log("## Unreleased\n\n### For admins\n- **Before you upgrade:** move it.\n\n")), "an upgrade admins must act on"),
        (render(*parse(change["CHANGELOG.md"])) == change["CHANGELOG.md"], "a file reads back as it was"),
    ]
    staging = image_plan("push", "staging", "abc1234", released["CHANGELOG.md"], {}, first)
    rerun = image_plan("push", "staging", "abc1234", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 7, 5))
    ship = image_plan("push", "main", "abc1234", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 8, 5))
    cases += [
        (staging.get("tags") == IMAGE + ":staging," + IMAGE + ":0.7.0004" and staging["record"] == "v0.7.0004"
         and staging["label"] == "0.7.0004", "staging publishes the number and records its tag"),
        (rerun.get("mode") == "build" and rerun["record"] == "", "a re-run of a staging publish keeps its number"),
        ("error" in image_plan("push", "staging", "abc1234", change["CHANGELOG.md"], {}, first), "staging never builds Unreleased"),
        ("error" in image_plan("push", "staging", "def5678", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 7, 5)),
         "a number is never published twice"),
        (ship.get("mode") == "retag" and ship["source"] == IMAGE + ":0.7.0004" and ship["tags"] == IMAGE + ":0.7," + IMAGE + ":latest",
         "ship it retags the image staging published, without building"),
        ("error" in image_plan("push", "main", "def5678", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 8, 5)),
         "main only ships a commit staging published"),
        (image_plan("workflow_dispatch", "x", "abc1234567", "", {}, first, "test-a").get("label") == "0.7.0004-test.abc1234",
         "a test publish is labelled, not numbered"),
    ]
    try:
        merge(change["CHANGELOG.md"], change["CHANGELOG.md"].replace("clearer", "changed"))
        cases.append((False, "a numbered version that reads differently isn't merged"))
    except ValueError:
        cases.append((True, "a numbered version that reads differently isn't merged"))

    failed = [name for passed, name in cases if not passed]
    if failed:
        print("SELF-TEST FAILED: " + "; ".join(failed))
        return 1
    print("Build numbers self-test passed (%d cases)." % len(cases))
    return 0


# ---- command line -----------------------------------------------------------------------------------------------

def read_dir(path):
    found = {}
    for name in FILES:
        with open(os.path.join(path, name), encoding="utf-8") as handle:
            found[name] = handle.read()
    return found


def write_dir(path, documents):
    for name, document in documents.items():
        with open(os.path.join(path, name), "w", encoding="utf-8", newline="\n") as handle:
            handle.write(document)


def main(argv):
    args = argv[1:]
    if args == ["--self-test"]:
        return self_test()
    if args[:1] == ["next"]:
        changelog = args[args.index("--changelog") + 1] if "--changelog" in args else None
        upgrade = False
        if changelog:
            with open(changelog, encoding="utf-8") as handle:
                upgrade = upgrade_needs_admins(handle.read())
        shipping = parse_version(args[args.index("--shipped") + 1]) if "--shipped" in args else None
        upcoming = next_from_git(shipping=shipping)
        print(text(next_from_git(major=True) if "--major" in args or (upgrade and upcoming[0] >= 1) else upcoming))
        return 0
    if len(args) in (4, 5) and args[0] == "image":
        with open("CHANGELOG.md", encoding="utf-8") as handle:
            plan = image_plan(args[1], args[2], args[3], handle.read(), remote_tags(), next_from_git(), args[4] if len(args) == 5 else "")
        for key, value in plan.items():
            print("%s=%s" % (key, value))
        return 1 if "error" in plan else 0
    if len(args) == 3 and args[0] == "label":
        print("%s-%s.%s" % (text(next_from_git()), args[1], args[2][:7]))
        return 0
    if len(args) in (4, 5) and args[0] == "release":
        security = SECURITY_LINE if "--security" in args else None
        documents = read_dir(args[1])
        number = parse_version(args[2])
        write_dir(args[1], {name: release(documents[name], number, args[3], security if name == FILES[0] else None)
                            for name in FILES})
        return 0
    if len(args) == 4 and args[0] == "merge":
        ours, theirs = read_dir(args[1]), read_dir(args[2])
        write_dir(args[3], {name: merge(ours[name], theirs[name]) for name in FILES})
        return 0
    if len(args) >= 4 and args[0] == "check":
        flags = args[4:]
        upper = read_dir(flags.pop(0)) if flags and not flags[0].startswith("--") else None
        number = parse_version(flags[flags.index("--number") + 1]) if "--number" in flags else None
        problems = check(args[1], read_dir(args[2]), read_dir(args[3]), upper, number, "--no-changelog" in flags)
        for problem in problems:
            print(("::error::" if os.environ.get("GITHUB_ACTIONS") else "") + problem)
        return 1 if problems else 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
