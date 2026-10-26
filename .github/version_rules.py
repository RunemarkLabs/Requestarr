#!/usr/bin/env python3
"""Requestarr's version numbers follow the release flow (docs/development.md#versions):

  1. Patch = dev builds. Every pull request into dev bumps the patch number on dev's current line: 0.5.0, 0.5.1, ...
  2. Minor = QA cycles. A promotion to QA takes dev's version as it is; the next pull request into dev after it starts
     the next minor at .0 (QA has 0.4.17, so dev's next is 0.5.0).
  3. Staging adds a beta pre-release. QA's version is published to staging as x.y.z-beta.N; N goes up each time the
     same line (x.y) is published to staging again: a fix into staging, or a newer QA version of that line.
  4. Production removes it. "Ship it" releases staging's exact code as x.y.z.
  5. Majors. 1.0.0 is the first public release with the version 1 scope. After that a major only when admins must
     act on upgrade, said in a "**Before you upgrade:**" line under For admins; until 1.0 nothing triggers a major.

A fix into qa or staging takes the next patch on that branch's own line that none of dev, qa and staging has yet
(QA on 0.4.17 while dev is on 0.5.x: 0.4.18). Versions are never shared between the three branches. A merge-back keeps
every version of both branches, in version order, highest first, so each line's versions stay together.

  python3 .github/version_rules.py check <dev|qa|staging> <x.y.z> <CHANGELOG.md> < branches.json
        branches.json: {"dev": ["0.4.17", ...], "qa": [...], "staging": [...]}, each branch's versions newest first,
        the target's as the pull request's base has them. Exit 1 with the reason when the version breaks a rule.
  python3 .github/version_rules.py next <dev|qa|staging> < branches.json
        the version the next pull request into that branch takes
  python3 .github/version_rules.py beta <x.y.z> <commit> < tags
        staging's beta version for that commit, from `git ls-remote --tags origin` (v0.4.17-beta.1 tags record each
        publish; a commit already published keeps its number)
  python3 .github/version_rules.py merge <ours> <theirs> <out>
        a merge-back of CHANGELOG.md or QA-CHECKLIST.md: every version section of both, highest first
  python3 .github/version_rules.py --self-test
"""
import json
import re
import sys

VERSION = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")
HEADING = re.compile(r"^## (\d+)\.(\d+)\.(\d+)\b", re.MULTILINE)
BETA_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)-beta\.(\d+)$")
UPGRADE_LEAD = "- **Before you upgrade:**"


def parse(text):
    match = VERSION.match(text.strip())
    if not match:
        raise ValueError("not a version: %r" % text)
    return tuple(int(x) for x in match.groups())


def text(version):
    return "%d.%d.%d" % version


def line(version):
    return version[:2]


def versions_of(branches, name):
    return [parse(v) for v in branches.get(name) or []]


def used(branches):
    return {v for name in ("dev", "qa", "staging") for v in versions_of(branches, name)}


def next_dev(branches):
    """Rule 1 and 2: the patch after dev's top, or the next minor once QA (or staging) has dev's line."""
    dev = versions_of(branches, "dev")
    if not dev:
        return (0, 0, 1)
    top = dev[0]
    promoted = any(versions_of(branches, other) and line(versions_of(branches, other)[0]) == line(top)
                   for other in ("qa", "staging"))
    return (top[0], top[1] + 1, 0) if promoted else (top[0], top[1], top[2] + 1)


def next_fix(target, branches):
    """A fix into qa or staging: the next patch on that branch's line that no branch has yet."""
    top = versions_of(branches, target)[0]
    taken = used(branches)
    patch = top[2] + 1
    while (top[0], top[1], patch) in taken:
        patch += 1
    return (top[0], top[1], patch)


def next_version(target, branches):
    return next_dev(branches) if target == "dev" else next_fix(target, branches)


def section(changelog, version):
    """The lines of one version's section in a changelog."""
    lines, inside = [], False
    for row in changelog.splitlines():
        heading = HEADING.match(row)
        if heading:
            inside = tuple(int(x) for x in heading.groups()) == version
            continue
        if inside:
            lines.append(row)
    return lines


def admins_must_act(changelog, version):
    """A "**Before you upgrade:**" line under For admins: admins have something to do when they upgrade."""
    heading = None
    for row in section(changelog, version):
        if row.startswith("### "):
            heading = row[4:].strip()
        elif heading == "For admins" and row.startswith(UPGRADE_LEAD):
            return True
    return False


def check(target, new, changelog, branches):
    """None when `new` follows the rules for a pull request into `target`, else the reason it doesn't."""
    if target not in ("dev", "qa", "staging"):
        return None
    for other in ("dev", "qa", "staging"):
        if other != target and new in versions_of(branches, other):
            return "%s is already %s's: dev, qa and staging never share a version." % (text(new), other)
    must_act = admins_must_act(changelog, new)

    if target == "dev":
        top = versions_of(branches, "dev")[0] if versions_of(branches, "dev") else (0, 0, 0)
        expected = next_dev(branches)
        why = ("QA has dev's %s line, so the next pull request into dev starts the next minor" % ".".join(map(str, line(top)))
               if expected[2] == 0 else "every pull request into dev takes the next patch on dev's line")
        major = (top[0] + 1, 0, 0)
        if new == expected:
            if must_act and top[0] >= 1:
                return ("%s has a 'Before you upgrade' line: admins must act on upgrade, so it's a major: %s."
                        % (text(new), text(major)))
            return None
        if new == major:
            if top[0] == 0 and new == (1, 0, 0):
                return None
            if top[0] >= 1 and must_act:
                return None
            if top[0] >= 1:
                return ("%s is a major, which is only for a change admins must act on: add a '**Before you upgrade:**' "
                        "line under For admins saying what to do, or take %s." % (text(new), text(expected)))
            return "until 1.0 nothing triggers a major: take %s (%s)." % (text(expected), why)
        return "this pull request into dev is %s: %s, not %s." % (text(expected), why, text(new))

    expected = next_fix(target, branches)
    if new != expected:
        return ("a fix into %s takes the next patch on %s's own line that no branch has yet: %s, not %s."
                % (target, target, text(expected), text(new)))
    if must_act and versions_of(branches, target)[0][0] >= 1:
        return "a fix into %s can't need admins to act on upgrade: that's a major, which only starts on dev." % target
    return None


def beta(version, commit, refs):
    """Staging's x.y.z-beta.N for a commit: the number it already has, or one more than its line's highest."""
    tags = {}
    for row in refs.splitlines():
        parts = row.split()
        if len(parts) != 2 or not parts[1].startswith("refs/tags/"):
            continue
        name = parts[1][len("refs/tags/"):]
        peeled = name.endswith("^{}")
        name = name[:-3] if peeled else name
        if peeled or name not in tags:
            tags[name] = parts[0]
    highest = 0
    for name, sha in tags.items():
        match = BETA_TAG.match(name)
        if not match:
            continue
        tagged = tuple(int(x) for x in match.groups()[:3])
        number = int(match.group(4))
        if tagged == version and sha == commit:
            return "%s-beta.%d" % (text(version), number)
        if line(tagged) == line(version):
            highest = max(highest, number)
    return "%s-beta.%d" % (text(version), highest + 1)


def split(document):
    """The text before the first version, and each version's section by version."""
    matches = list(HEADING.finditer(document))
    if not matches:
        return document, {}
    sections = {}
    for i, match in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(document)
        body = document[match.start():end]
        sections[tuple(int(x) for x in match.groups())] = body if body.endswith("\n") else body + "\n"
    return document[:matches[0].start()], sections


def merge(ours, theirs):
    """Every version section of both, highest first; a version both have must read the same."""
    preamble, mine = split(ours)
    _, others = split(theirs)
    for version in mine.keys() & others.keys():
        if mine[version].rstrip() != others[version].rstrip():
            raise ValueError("%s reads differently on the two branches: merge it by hand" % text(version))
    merged = dict(others)
    merged.update(mine)
    body = "".join(merged[v] if merged[v].endswith("\n\n") else merged[v] + "\n" for v in sorted(merged, reverse=True))
    return preamble + body.rstrip("\n") + "\n"


def self_test():
    on = lambda dev, qa, staging: {"dev": dev, "qa": qa, "staging": staging}
    log = lambda *sections: "# Changelog\n\n" + "".join(sections)
    upgrade = "## 2.0.0 (2027-01-02)\n\n### For admins\n- **Before you upgrade:** move the bot settings into the new file.\n\n"
    plain = lambda v: "## %s (2026-10-27)\n\n" % v
    now = on(["0.4.17", "0.4.16"], ["0.4.17", "0.4.16"], ["0.4.2"])
    working = on(["0.5.2", "0.5.1", "0.5.0", "0.4.17"], ["0.4.17"], ["0.4.2"])
    qa_fixed = on(["0.5.2", "0.5.1", "0.5.0", "0.4.17"], ["0.4.18", "0.4.17"], ["0.4.17"])
    older = on(["0.5.5", "0.5.4", "0.5.3"], ["0.5.3"], ["0.4.17"])
    v1 = on(["1.2.3", "1.2.2"], ["1.1.4"], ["1.1.4"])
    ok = lambda target, new, branches, changelog=None: check(target, parse(new), changelog or log(plain(new)), branches) is None
    cases = [
        (ok("dev", "0.5.3", working), "a normal dev bump: the next patch"),
        (not ok("dev", "0.5.4", working), "a dev bump that skips a patch fails"),
        (not ok("dev", "0.6.0", working), "a dev bump to the next minor while QA has an older line fails"),
        (ok("dev", "0.5.0", now), "the first dev pull request after a promotion starts the next minor"),
        (not ok("dev", "0.4.18", now), "a patch after a promotion fails"),
        (not ok("dev", "0.5.1", now), "the next minor starts at .0"),
        (ok("dev", "0.6.0", older), "QA took an older version of dev's line: still the next minor"),
        (ok("qa", "0.4.18", working), "a QA fix: the next patch on QA's line"),
        (not ok("qa", "0.5.3", working), "a QA fix on dev's line fails"),
        (not ok("qa", "0.4.19", working), "a QA fix that skips a free patch fails"),
        (ok("qa", "0.5.6", older), "a QA fix skips patches dev already has"),
        (ok("staging", "0.4.18", on(["0.5.0", "0.4.17"], ["0.4.17"], ["0.4.17"])), "a staging fix: the next patch on staging's line"),
        (ok("staging", "0.4.19", qa_fixed), "a staging fix skips QA's fix number"),
        (not ok("staging", "0.4.18", qa_fixed), "a staging fix can't share QA's version"),
        (not ok("staging", "0.5.0", qa_fixed), "a staging fix on another line fails"),
        (ok("dev", "1.0.0", on(["0.9.3"], ["0.8.1"], [])), "1.0.0 from 0.x is the first public release"),
        (not ok("dev", "2.0.0", on(["0.9.3"], ["0.8.1"], [])), "until 1.0 there's no other major"),
        (not ok("dev", "2.0.0", v1), "a major without a 'Before you upgrade' line fails"),
        (ok("dev", "2.0.0", v1, log(upgrade)), "a major with a 'Before you upgrade' line passes"),
        (not ok("dev", "1.2.4", v1, log(upgrade.replace("2.0.0", "1.2.4"))), "from 1.0, admins acting on upgrade means a major"),
        (ok("dev", "0.5.3", working, log(upgrade.replace("2.0.0", "0.5.3"))), "before 1.0 a 'Before you upgrade' line is fine"),
        (not ok("dev", "0.4.17", on(["0.4.16"], ["0.4.17"], [])), "dev can't take a version QA has"),
        (next_version("dev", now) == (0, 5, 0) and next_version("dev", working) == (0, 5, 3), "next version for dev"),
        (next_version("qa", working) == (0, 4, 18) and next_version("staging", qa_fixed) == (0, 4, 19), "next fix versions"),
        (next_version("dev", on(["0.4.18", "0.4.17"], ["0.4.18", "0.4.17"], [])) == (0, 5, 0),
         "a QA fix merged back into a dev that hadn't moved: dev's next is the next minor"),
        (next_version("dev", on(["0.5.0", "0.4.18", "0.4.17"], ["0.4.18", "0.4.17"], [])) == (0, 5, 1),
         "a QA fix merged back below dev's new line: dev carries on"),
    ]
    refs = "\n".join([
        "aaa\trefs/tags/v0.4.17-beta.1",
        "bbb\trefs/tags/v0.4.18-beta.2",
        "ccc\trefs/tags/v0.3.9-beta.4",
        "ddd\trefs/tags/v0.4.18-beta.2^{}",
        "eee\trefs/tags/v0.4.0",
    ])
    cases += [
        (beta((0, 4, 17), "new", "") == "0.4.17-beta.1", "the first beta of a line is beta.1"),
        (beta((0, 4, 19), "new", refs) == "0.4.19-beta.3", "the same line again: one more than its highest"),
        (beta((0, 4, 17), "aaa", refs) == "0.4.17-beta.1", "a commit already published keeps its number"),
        (beta((0, 4, 18), "ddd", refs) == "0.4.18-beta.2", "an annotated tag is read by the commit it points to"),
        (beta((0, 5, 3), "new", refs) == "0.5.3-beta.1", "a new line starts at beta.1 again"),
    ]
    dev_log = "# Changelog\n\nintro\n\n## 0.5.1 (2026-10-29)\n- one\n\n## 0.5.0 (2026-10-28)\n\n## 0.4.17 (2026-10-26)\n- old\n"
    qa_log = "# Changelog\n\nintro\n\n## 0.4.18 (2026-10-30)\n- **Fix:** a QA fix.\n\n## 0.4.17 (2026-10-26)\n- old\n"
    merged = merge(dev_log, qa_log)
    order = [m.group(0)[3:] for m in HEADING.finditer(merged)]
    cases += [
        (order == ["0.5.1", "0.5.0", "0.4.18", "0.4.17"] and merged.startswith("# Changelog\n\nintro\n\n"),
         "a merge-back keeps every version, highest first"),
        ("## 0.5.0 (2026-10-28)\n\n## 0.4.18" in merged and merged.endswith("- old\n"), "a heading-only version stays as it is"),
    ]
    try:
        merge(dev_log, qa_log.replace("- old", "- changed"))
        cases.append((False, "a version that reads differently isn't merged"))
    except ValueError:
        cases.append((True, "a version that reads differently isn't merged"))

    failed = [name for passed, name in cases if not passed]
    if failed:
        print("SELF-TEST FAILED: " + "; ".join(failed))
        return 1
    print("Version rules self-test passed (%d cases)." % len(cases))
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) == 5 and argv[1] == "check":
        with open(argv[4], encoding="utf-8") as changelog:
            problem = check(argv[2], parse(argv[3]), changelog.read(), json.load(sys.stdin))
        if problem:
            print(problem)
            return 1
        return 0
    if len(argv) == 3 and argv[1] == "next":
        print(text(next_version(argv[2], json.load(sys.stdin))))
        return 0
    if len(argv) == 4 and argv[1] == "beta":
        print(beta(parse(argv[2]), argv[3], sys.stdin.read()))
        return 0
    if len(argv) == 5 and argv[1] == "merge":
        with open(argv[2], encoding="utf-8") as a, open(argv[3], encoding="utf-8") as b:
            merged = merge(a.read(), b.read())
        with open(argv[4], "w", encoding="utf-8", newline="\n") as out:
            out.write(merged)
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
