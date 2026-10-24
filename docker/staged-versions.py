"""Prints the versions staging has, comma separated, for the QA stack's what's-new posts (docs/development.md#version-posts).

Staging has what was last published for it: the commit of the newest successful Image workflow run on the staging
branch, from GitHub's public record (no login). That commit's CHANGELOG.md lists the versions. Before anything is
published for staging it prints nothing, so QA shows every version. If GitHub can't be reached it falls back to the
staging branch's changelog and says so. Run by `dev.ps1` / `dev.sh` when they start the QA stack.
"""
import json
import os
import re
import subprocess
import sys
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNS = ("https://api.github.com/repos/RunemarkLabs/Requestarr/actions/workflows/image.yml/runs"
        "?branch=staging&event=push&status=success&per_page=1")


def git(*args):
    return subprocess.run(["git", "-C", REPO, *args], capture_output=True, text=True, encoding="utf-8")


def versions(ref):
    shown = git("show", f"{ref}:CHANGELOG.md")
    return [] if shown.returncode else re.findall(r"^## (\d+\.\d+\.\d+)\s*$", shown.stdout, re.MULTILINE)


def published_commit():
    request = urllib.request.Request(RUNS, headers={"User-Agent": "requestarr-dev-stack", "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(request, timeout=15) as response:
        runs = json.load(response)["workflow_runs"]
    return runs[0]["head_sha"] if runs else None


def main():
    try:
        commit = published_commit()
    except Exception as error:  # offline, rate limited: the branch is the next best record
        print(f"staged-versions: couldn't ask GitHub ({error}); using the staging branch instead", file=sys.stderr)
        print(",".join(versions("origin/staging")))
        return

    if commit is None:
        print("")
        return
    if git("cat-file", "-e", f"{commit}^{{commit}}").returncode:
        git("fetch", "-q", "origin", "staging")
    print(",".join(versions(commit)))


if __name__ == "__main__":
    main()
