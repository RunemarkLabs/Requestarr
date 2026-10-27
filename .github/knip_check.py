#!/usr/bin/env python3
"""Unused files, exports, types and dependencies in the web app (knip), checked against a baseline.

knip has no baseline of its own, so this keeps one: frontend/knip-baseline.json lists what knip found when the check
was added. The check fails on anything knip finds that isn't in the baseline, and on baseline entries knip no longer
finds (remove them with --update), so the baseline can only shrink (docs/development.md#clean-code).

  python3 .github/knip_check.py            run knip (node_modules/.bin/knip) and compare
  python3 .github/knip_check.py --update   rewrite the baseline to what knip finds now
  python3 .github/knip_check.py --self-test
"""
import json
import os
import subprocess
import sys

BASELINE = os.path.join("frontend", "knip-baseline.json")
# Kinds of finding that mean code or packages nothing uses.
KINDS = ["files", "dependencies", "devDependencies", "unlisted", "unresolved", "exports", "types", "enumMembers",
         "duplicates"]


def findings(report):
    """Every finding as "kind: file: name", sorted."""
    found = set()
    for issue in report.get("issues", []):
        for kind in KINDS:
            entries = issue.get(kind) or []
            # Enum members come grouped by enum.
            if isinstance(entries, dict):
                entries = [item for group in entries.values() for item in group]
            for entry in entries:
                # A duplicate is a list of the names one export goes by; every other finding is a single name.
                if isinstance(entry, dict):
                    name = entry.get("name")
                else:
                    name = "+".join(e.get("name", "") for e in entry)
                found.add("%s: %s: %s" % (kind, issue["file"], name))
    return sorted(found)


def parse(output):
    """knip's JSON report; anything the webpack config printed while knip loaded it comes first."""
    start = output.find('{"issues"')
    if start < 0:
        raise ValueError("knip printed no report:\n" + output[-2000:])
    return json.loads(output[start:])


def compare(now, baseline):
    """(new findings, baseline entries no longer found)"""
    return sorted(set(now) - set(baseline)), sorted(set(baseline) - set(now))


def run_knip():
    knip = os.path.join("node_modules", ".bin", "knip.cmd" if os.name == "nt" else "knip")
    result = subprocess.run([knip, "--reporter", "json", "--no-exit-code"], capture_output=True, text=True,
                            encoding="utf-8")
    return findings(parse(result.stdout))


def self_test():
    report = {"issues": [
        {"file": "a.ts", "files": [{"name": "a.ts"}], "exports": [{"name": "unused"}], "types": []},
        {"file": "b.js", "duplicates": [[{"name": "x"}, {"name": "y"}]], "dependencies": []},
    ]}
    now = findings(parse("webpack says hello\n" + json.dumps(report)))
    cases = [
        (now == ["duplicates: b.js: x+y", "exports: a.ts: unused", "files: a.ts: a.ts"], "findings are read"),
        (compare(now, now) == ([], []), "the same findings pass"),
        (compare(now, now[1:]) == (now[:1], []), "a new finding fails"),
        (compare(now[1:], now) == ([], now[:1]), "a fixed finding must leave the baseline"),
    ]
    failed = [name for ok, name in cases if not ok]
    print("SELF-TEST FAILED: " + "; ".join(failed) if failed else "knip check self-test passed (%d cases)." % len(cases))
    return 1 if failed else 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    now = run_knip()
    if argv[1:] == ["--update"]:
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(now, handle, indent=2)
            handle.write("\n")
        print("Baseline: %d findings in %s." % (len(now), BASELINE))
        return 0
    with open(BASELINE, encoding="utf-8") as handle:
        baseline = json.load(handle)
    new, gone = compare(now, baseline)
    for line in new:
        print("::error::Unused in the web app (knip): %s" % line)
    for line in gone:
        print("::error::No longer found, so remove it from %s (python3 .github/knip_check.py --update): %s"
              % (BASELINE, line))
    if new or gone:
        return 1
    print("knip: nothing new is unused (%d known findings in the baseline)." % len(baseline))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
