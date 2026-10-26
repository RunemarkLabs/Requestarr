#!/usr/bin/env python3
"""The gate before staging: is QA's test checklist complete, with no item failing and no bug report from QA still open?

Run through the stack scripts, which pass the stack's address and API key (never printed):
    .\\docker\\dev.ps1 qa-gate          docker/dev.sh qa-gate          ./release.sh qa-gate
(-Stack dev / --stack dev reads the dev stack instead, where the checklist is tried out.)

Reads the checklist from Requestarr (GET /api/v1/qa/checklist) and the open issues from GitHub (gh): any open issue
labelled with this server's environment ("env: qa") and a version the checklist covers counts, whether or not it came
from a checklist item. A report from the server that isn't on GitHub (filing off, no key, or GitHub refused it) blocks
too, whatever its version: it's an open QA bug the issue list can't show.

Exit 0: clear (staging may have these versions). 1: blocked. 2: couldn't tell (stack down, gh missing).
--json prints the verdict as JSON for release.sh. --self-test checks the verdict rules (CI runs it).
"""
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

REPOSITORY = os.environ.get("BUG_REPORTS_REPOSITORY", "RunemarkLabs/Requestarr")
WHY = {"Off": "filing is turned off on the server", "NoToken": "the server has no GitHub access key",
       "Pending": "it was being filed when the server stopped", "Failed": "GitHub refused it or couldn't be reached"}


def checklist():
    # A saved answer instead of the stack (the live test writes one at each step, in the same format).
    if os.environ.get("QA_GATE_FILE"):
        with open(os.environ["QA_GATE_FILE"], encoding="utf-8") as handle:
            return json.load(handle)
    url = os.environ["QA_GATE_URL"].rstrip("/") + "/api/v1/qa/checklist"
    request = urllib.request.Request(url, headers={"X-Api-Key": os.environ.get("REQUESTARR_API_KEY", ""), "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=15) as response:
        return json.load(response)


def open_issues(environment, versions):
    out = subprocess.run(["gh", "issue", "list", "--repo", REPOSITORY, "--state", "open", "--label", "env: " + environment,
                          "--limit", "500", "--json", "number,title,url,labels"], capture_output=True, text=True, check=True).stdout
    wanted = {"version: " + v for v in versions}
    return [i for i in json.loads(out) if wanted & {label["name"] for label in i["labels"]}]


def decide(data, issues):
    """The verdict from the server's answer and the open issues: clear only when every item passed, none is failing,
    no issue from this server is open and every report from it is on GitHub."""
    state = data["checklist"]
    # Older servers sent only the reports for the checklist's versions.
    unfiled = data["unfiledReports"] if "unfiledReports" in data else [r for r in data.get("bugReports", []) if r.get("filing") != "Filed"]
    complete = state["complete"]
    clear = complete and state["failing"] == 0 and not issues and not unfiled
    verdict = {
        "clear": clear, "environment": state["environment"], "versions": state["versions"], "total": state["total"], "passed": state["passed"],
        "failing": state["failing"], "complete": complete, "completedBy": state.get("completedBy"), "completedAt": state.get("completedAt"),
        "openIssues": [{"number": i["number"], "title": i["title"], "url": i["url"]} for i in issues],
        "unfiledReports": [{"id": r["id"], "filing": r.get("filing"), "problem": r.get("filingProblem")} for r in unfiled],
    }
    return verdict, unfiled


def self_test():
    state = {"environment": "qa", "versions": ["0.4.16"], "total": 2, "passed": 2, "failing": 0, "complete": True, "items": []}
    issue = {"number": 9, "title": "t", "url": "u"}
    kept = {"id": 3, "filing": "NoToken", "filingProblem": None}
    cases = [
        ("complete, nothing open", {"checklist": state, "unfiledReports": []}, [], True),
        ("an open issue", {"checklist": state, "unfiledReports": []}, [issue], False),
        ("a report not on GitHub", {"checklist": state, "unfiledReports": [kept]}, [], False),
        ("an older server's unfiled report", {"checklist": state, "bugReports": [dict(kept, filing="Off")]}, [], False),
        ("an older server's filed report", {"checklist": state, "bugReports": [dict(kept, filing="Filed")]}, [], True),
        ("a failing item", {"checklist": dict(state, failing=1, passed=1), "unfiledReports": []}, [], False),
    ]
    wrong = [name for name, data, issues, clear in cases if decide(data, issues)[0]["clear"] != clear]
    for name in wrong:
        print("qa-gate self-test: wrong verdict for %s" % name, file=sys.stderr)
    print("qa-gate self-test: %d of %d cases right" % (len(cases) - len(wrong), len(cases)))
    return 1 if wrong else 0


def main(argv):
    if "--self-test" in argv:
        return self_test()
    as_json = "--json" in argv
    try:
        data = checklist()
    except urllib.error.HTTPError as error:
        reason = 'it runs a Requestarr from before the QA checklist' if error.code == 404 else 'HTTP %d' % error.code
        print('qa-gate: the stack has no QA checklist to read (%s).' % reason, file=sys.stderr)
        return 2
    except (urllib.error.URLError, OSError, ValueError, KeyError) as error:
        print("qa-gate: couldn't read the checklist from the stack (%s). Is it running?" % type(error).__name__, file=sys.stderr)
        return 2

    state = data["checklist"]
    versions = state["versions"]
    try:
        issues = open_issues(state["environment"], versions) if versions else []
    except (OSError, subprocess.CalledProcessError) as error:
        print("qa-gate: couldn't list the open issues on GitHub with gh (%s)." % type(error).__name__, file=sys.stderr)
        return 2

    verdict, unfiled = decide(data, issues)
    clear, complete = verdict["clear"], verdict["complete"]

    if as_json:
        print(json.dumps(verdict))
        return 0 if clear else 1

    print("QA checklist (%s server) for %s: %d of %d passed, %d failing%s." % (
        state["environment"], ", ".join(versions) or "no versions", state["passed"], state["total"], state["failing"],
        ", complete, the last item passed by %s at %s" % (state.get("completedBy"), state.get("completedAt")) if complete else ""))
    for item in state["items"]:
        if item["state"] != "Passed":
            print("  %-8s %s: %s%s" % (item["state"].lower(), item["version"], item["name"], " (%s)" % item["issueUrl"] if item.get("issueUrl") else ""))
    print("Open bug reports from %s for these versions on GitHub: %d" % (state["environment"], len(issues)))
    for issue in issues:
        print("  #%d %s  %s" % (issue["number"], issue["title"], issue["url"]))
    print("Bug reports from %s not on GitHub: %d" % (state["environment"], len(unfiled)))
    for report in unfiled:
        print("  #%d %s: %s" % (report["id"], report.get("filing"), report.get("filingProblem") or WHY.get(report.get("filing"), "not filed")))
    print("CLEAR: staging may have %s." % ", ".join(versions) if clear else
          "BLOCKED: promotion to staging waits until every item has passed and every bug report from QA is on GitHub and closed "
          "(or the owner overrides).")
    return 0 if clear else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
