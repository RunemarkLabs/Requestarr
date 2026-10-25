#!/usr/bin/env python3
"""The gate before staging: is QA's test checklist complete, with no item failing and no bug report from QA still open?

Run through the stack scripts, which pass the stack's address and API key (never printed):
    .\\docker\\dev.ps1 qa-gate          docker/dev.sh qa-gate          ./release.sh qa-gate
(-Stack dev / --stack dev reads the dev stack instead, where the checklist is tried out.)

Reads the checklist from Requestarr (GET /api/v1/qa/checklist) and the open issues from GitHub (gh): any open issue
labelled with this server's environment ("env: qa") and a version the checklist covers counts, whether or not it came
from a checklist item. A report Requestarr couldn't file on GitHub is listed too, so nobody forgets it.

Exit 0: clear (staging may have these versions). 1: blocked. 2: couldn't tell (stack down, gh missing).
--json prints the verdict as JSON for release.sh.
"""
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

REPOSITORY = os.environ.get("BUG_REPORTS_REPOSITORY", "RunemarkLabs/Requestarr")


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


def main(argv):
    as_json = "--json" in argv
    try:
        data = checklist()
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

    unfiled = [r for r in data["bugReports"] if r["filing"] != "Filed"]
    complete = state["complete"]
    clear = complete and state["failing"] == 0 and not issues
    verdict = {
        "clear": clear, "environment": state["environment"], "versions": versions, "total": state["total"], "passed": state["passed"],
        "failing": state["failing"], "complete": complete, "completedBy": state["completedBy"], "completedAt": state["completedAt"],
        "openIssues": [{"number": i["number"], "title": i["title"], "url": i["url"]} for i in issues],
        "unfiledReports": [r["id"] for r in unfiled],
    }

    if as_json:
        print(json.dumps(verdict))
        return 0 if clear else 1

    print("QA checklist (%s server) for %s: %d of %d passed, %d failing%s." % (
        state["environment"], ", ".join(versions) or "no versions", state["passed"], state["total"], state["failing"],
        ", complete, the last item passed by %s at %s" % (state["completedBy"], state["completedAt"]) if complete else ""))
    for item in state["items"]:
        if item["state"] != "Passed":
            print("  %-8s %s: %s%s" % (item["state"].lower(), item["version"], item["name"], " (%s)" % item["issueUrl"] if item.get("issueUrl") else ""))
    print("Open bug reports from %s for these versions on GitHub: %d" % (state["environment"], len(issues)))
    for issue in issues:
        print("  #%d %s  %s" % (issue["number"], issue["title"], issue["url"]))
    if unfiled:
        print("Reports kept on the server but not on GitHub: %s (check them in the bug reports channel)." % ", ".join("#%d" % r["id"] for r in unfiled))
    print("CLEAR: staging may have %s." % ", ".join(versions) if clear else
          "BLOCKED: promotion to staging waits until every item has passed and no bug report from QA is open (or the owner overrides).")
    return 0 if clear else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
