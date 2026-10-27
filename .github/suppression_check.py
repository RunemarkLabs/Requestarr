#!/usr/bin/env python3
"""A pull request may not quietly switch the clean-code checks off (docs/development.md#clean-code). It fails when the
change, compared with its base:

  - adds a suppression without a reason on the same line: `#pragma warning disable` with no `//` comment,
    `[SuppressMessage]` with no Justification, `eslint-disable` with no `-- reason`;
  - grows a baseline: a new or higher count in frontend/eslint-suppressions.json, a new entry in
    frontend/knip-baseline.json (the pull request that creates a baseline with its check is the exception);
  - turns a rule down in .editorconfig's "Requestarr additions" (a lower severity, or a rule removed), or raises a
    limit in src/SonarLint.xml.

  python3 .github/suppression_check.py <base commit> [<head commit>]
  python3 .github/suppression_check.py --self-test
"""
import json
import re
import subprocess
import sys

SEVERITY = {"none": 0, "silent": 1, "suggestion": 2, "warning": 3, "error": 4}
ADDITIONS = "# Requestarr additions."


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8").stdout


def show(commit, path):
    result = subprocess.run(["git", "show", "%s:%s" % (commit, path)], capture_output=True, text=True, encoding="utf-8")
    return result.stdout if result.returncode == 0 else None


def added_lines(diff):
    """(file, line) for every line the diff adds."""
    path = None
    for row in diff.splitlines():
        if row.startswith("+++ "):
            path = row[6:] if row.startswith("+++ b/") else None
        elif row.startswith("+") and not row.startswith("+++") and path:
            yield path, row[1:]


def unexplained(path, line):
    """Why an added line is a suppression without a reason, or None."""
    if path.endswith(".cs"):
        if "#pragma warning disable" in line and "//" not in line.split("#pragma warning disable", 1)[1]:
            return "#pragma warning disable without a reason comment on the same line"
        if re.search(r"\bSuppressMessage\(", line) and not re.search(r'Justification\s*=\s*"[^"]+"', line):
            return "[SuppressMessage] without a Justification"
    if (re.search(r"\.(js|jsx|ts|tsx)$", path) and "eslint-disable" in line
            and not re.search(r"eslint-disable\S*\s+[^*]*?\s--\s+\S", line)):
        return "eslint-disable without '-- reason'"
    return None


def suppression_counts(text):
    counts = {}
    for path, rules in (json.loads(text) if text else {}).items():
        for rule, entry in rules.items():
            counts[(path, rule)] = entry.get("count", 0)
    return counts


def grown_suppressions(base, head):
    before, after = suppression_counts(base), suppression_counts(head)
    return ["%s: %s goes from %d to %d" % (path, rule, before.get((path, rule), 0), count)
            for (path, rule), count in sorted(after.items()) if count > before.get((path, rule), 0)]


def grown_baseline(base, head):
    return sorted(set(json.loads(head) if head else []) - set(json.loads(base) if base else []))


def severities(editorconfig):
    """{rule: severity} in the Requestarr additions section."""
    if not editorconfig or ADDITIONS not in editorconfig:
        return {}
    found = {}
    for row in editorconfig.split(ADDITIONS, 1)[1].splitlines():
        match = re.match(r"\s*(dotnet_diagnostic\.\w+\.severity|dotnet_naming_rule\.\w+\.severity)\s*=\s*(\w+)", row)
        if match:
            found[match.group(1)] = match.group(2).lower()
    return found


def lowered(base, head):
    before, after = severities(base), severities(head)
    return ["%s: %s -> %s" % (rule, level, after.get(rule, "removed")) for rule, level in sorted(before.items())
            if SEVERITY.get(after.get(rule, "none"), 0) < SEVERITY.get(level, 0)]


def limits(xml):
    return {(rule, key): int(value) for rule, key, value in re.findall(
        r"<Key>(S\d+)</Key>\s*<Parameters>.*?<Key>(\w+)</Key>\s*<Value>(\d+)</Value>", xml or "", re.S)}


def raised(base, head):
    before, after = limits(base), limits(head)
    return ["%s %s: %d -> %d" % (rule, key, value, after[(rule, key)]) for (rule, key), value in sorted(before.items())
            if (rule, key) in after and after[(rule, key)] > value]


def check(base, head):
    problems = []
    diff = git("diff", "--unified=0", base, head, "--", "*.cs", "*.js", "*.jsx", "*.ts", "*.tsx")
    for path, line in added_lines(diff):
        why = unexplained(path, line)
        if why:
            problems.append("%s: %s: %s" % (path, why, line.strip()))
    # A baseline the base doesn't have yet is being created with its check, not grown.
    sup = "frontend/eslint-suppressions.json"
    base_sup = show(base, sup)
    if base_sup is not None:
        problems += ["%s grows: %s" % (sup, p) for p in grown_suppressions(base_sup, show(head, sup))]
    knip = "frontend/knip-baseline.json"
    base_knip = show(base, knip)
    if base_knip is not None:
        problems += ["%s gains: %s" % (knip, p) for p in grown_baseline(base_knip, show(head, knip))]
    problems += [".editorconfig turns a Requestarr rule down: " + p
                 for p in lowered(show(base, ".editorconfig"), show(head, ".editorconfig"))]
    problems += ["src/SonarLint.xml raises a limit: " + p
                 for p in raised(show(base, "src/SonarLint.xml"), show(head, "src/SonarLint.xml"))]
    return problems


def self_test():
    editor = "[*.cs]\nx = 1\n# Requestarr additions. Everything above\n[{a}]\ndotnet_diagnostic.S138.severity = warning\n"
    xml = "<Rule>\n<Key>S138</Key>\n<Parameters>\n<Parameter>\n<Key>max</Key>\n<Value>60</Value>"
    cases = [
        (unexplained("a.cs", "#pragma warning disable S138") is not None, "a pragma without a reason fails"),
        (unexplained("a.cs", "#pragma warning disable S138 // a table of Discord's limits, one per line") is None,
         "a pragma with a reason passes"),
        (unexplained("a.cs", '[SuppressMessage("x", "S1")]') is not None, "SuppressMessage without Justification fails"),
        (unexplained("a.cs", '[SuppressMessage("x", "S1", Justification = "called by reflection")]') is None,
         "SuppressMessage with Justification passes"),
        (unexplained("a.tsx", "// eslint-disable-next-line max-params") is not None, "eslint-disable without reason fails"),
        (unexplained("a.tsx", "// eslint-disable-next-line max-params -- mirrors the upstream API") is None,
         "eslint-disable with a reason passes"),
        (grown_suppressions('{"a.js": {"r": {"count": 1}}}', '{"a.js": {"r": {"count": 2}}}') != [], "a higher count fails"),
        (grown_suppressions('{"a.js": {"r": {"count": 2}}}', '{"a.js": {"r": {"count": 1}}}') == [], "a lower count passes"),
        (grown_suppressions('{}', '{"b.js": {"r": {"count": 1}}}') != [], "a new suppressed file fails"),
        (grown_baseline('["x"]', '["x", "y"]') == ["y"] and grown_baseline('["x", "y"]', '["x"]') == [],
         "the knip baseline may only shrink"),
        (lowered(editor, editor.replace("warning", "suggestion")) != [], "a lowered severity fails"),
        (lowered(editor, editor.replace("dotnet_diagnostic.S138.severity = warning\n", "")) != [], "a removed rule fails"),
        (lowered(editor, editor.replace("warning", "error")) == [], "a raised severity passes"),
        (lowered(editor, editor.replace("# Requestarr additions. Everything above\n", "")) != [],
         "removing the whole section fails"),
        (raised(xml, xml.replace("60", "80")) != [] and raised(xml, xml.replace("60", "40")) == [], "a raised limit fails"),
    ]
    failed = [name for ok, name in cases if not ok]
    print("SELF-TEST FAILED: " + "; ".join(failed) if failed else "Suppression check self-test passed (%d cases)." % len(cases))
    return 1 if failed else 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) not in (2, 3):
        print(__doc__)
        return 2
    problems = check(argv[1], argv[2] if len(argv) == 3 else "HEAD")
    for problem in problems:
        print("::error::%s (docs/development.md#clean-code)" % problem)
    if not problems:
        print("No unexplained suppressions, no baseline growth, no rule turned down.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
