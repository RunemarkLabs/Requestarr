#!/usr/bin/env python3
"""Dependabot's pull requests into dev (docs/development.md#dependency-updates). The Dependency updates workflow runs it.

  sort      a new or changed update: label it "automatic update" (patch or minor) or "needs review" (major, or a
            change Dependabot can't size), plus "security" for a security fix and "for admins" when that fix is in
            something Requestarr ships (the backend, the base images, or a web app package the built app contains).
  update    the oldest open "automatic update": wait for the build check on Dependabot's own commit and merge it once
            it passes (hold it if it fails). Nothing is committed to the update: it has no version or changelog lines
            (a dependency update changes nothing users or admins see; a security fix in something Requestarr ships is
            named in the next build's For admins section by ./release.sh promote-staging). One at a time, oldest first.
  --self-test  checks the parts that decide, against built-in samples.

Needs the GitHub CLI (GH_TOKEN); sort also reads package.json and yarn.lock from the checkout.
"""
import json
import os
import re
import subprocess
import sys
import time

AUTOMATIC = "automatic update"
REVIEW = "needs review"
SECURITY = "security"
FOR_ADMINS = "for admins"
LABELS = {
    AUTOMATIC: ("0e8a16", "Patch or minor dependency update: merges into dev on its own once build passes"),
    REVIEW: ("d93f0b", "Major dependency update, or one that failed: waits for Claude Code to review"),
    SECURITY: ("b60205", "Fixes a published security problem"),
    FOR_ADMINS: ("5319e7", "Security fix in something Requestarr ships: its changelog tells admins"),
}

# Dependabot's own names for a grouped security update when the repository groups them (before dependabot.yml did).
DEFAULT_SECURITY_GROUPS = {"npm_and_yarn", "nuget", "github_actions", "docker"}

# The build check's states that end an update's wait: passed, or held for review.
FAILED = ("FAILURE", "CANCELLED", "TIMED_OUT", "ERROR", "STARTUP_FAILURE", "ACTION_REQUIRED", "STALE")


def run(*args, check=True):
    result = subprocess.run(args, check=False, text=True, capture_output=True)
    if check and result.returncode != 0:
        sys.stderr.write(result.stderr or "")
        raise SystemExit("%s failed (exit %d)" % (" ".join(args), result.returncode))
    return (result.stdout or "").strip()


def gh(*args, check=True):
    return run("gh", *args, check=check)


# ---- deciding ---------------------------------------------------------------------------------------------------

def automatic(update_type):
    return update_type in ("version-update:semver-patch", "version-update:semver-minor")


def _version(text):
    numbers = re.findall(r"\d+", text.lstrip("vV"))[:3]
    return tuple(int(n) for n in numbers) + (0,) * (3 - len(numbers)) if numbers else None


def size_from_text(text):
    """The biggest change among the "from A to B" lines of Dependabot's commit message, named the way fetch-metadata
    names it, for when fetch-metadata can't read the message (Dependabot's newer grouped format)."""
    pairs = re.findall(r"(?:Updates|Bump) `?[^`\s]+`? from (\S+) to (\S+?)(?:\s|$)", text)
    rank = 0
    for old, new in pairs:
        a, b = _version(old), _version(new)
        if a is None or b is None:
            return ""
        rank = max(rank, 3 if a[0] != b[0] else 2 if a[1] != b[1] else 1)
    return {1: "version-update:semver-patch", 2: "version-update:semver-minor", 3: "version-update:semver-major"}.get(rank, "")


def group_from_text(text):
    match = re.search(r"Bumps the (\S+) group", text)
    return match.group(1) if match else ""


def is_security(group):
    return bool(group) and (group.endswith("security") or group in DEFAULT_SECURITY_GROUPS)


def parse_yarn_lock(text):
    """{"name@range": (name, {dependency name: range})} for a yarn 1 lock file."""
    entries, keys, deps, in_deps = {}, None, None, False
    for line in text.splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        if not line.startswith(" "):
            keys = [k.strip().strip('"') for k in line.rstrip(":").split(",")]
            deps, in_deps = {}, False
            for key in keys:
                entries[key] = (key.rsplit("@", 1)[0], deps)
            continue
        stripped = line.strip()
        if line.startswith("  ") and not line.startswith("    "):
            in_deps = stripped in ("dependencies:", "optionalDependencies:")
            continue
        if in_deps and line.startswith("    "):
            name, _, wanted = stripped.partition(" ")
            deps[name.strip('"')] = wanted.strip().strip('"')
    return entries


def shipped_npm_packages(package_json, yarn_lock):
    """Every package the built web app can contain: the dependencies (not devDependencies) and all they pull in."""
    entries = parse_yarn_lock(yarn_lock)
    todo = ["%s@%s" % (n, r) for n, r in json.loads(package_json).get("dependencies", {}).items()]
    seen, names = set(), set()
    while todo:
        key = todo.pop()
        if key in seen or key not in entries:
            continue
        seen.add(key)
        name, deps = entries[key]
        names.add(name)
        todo.extend("%s@%s" % d for d in deps.items())
    return names


def ships(ecosystem, dependency_names, package_json="", yarn_lock=""):
    """Whether an update changes something Requestarr ships: the backend, the image, or the built web app."""
    if ecosystem in ("nuget", "docker", "docker_compose"):
        return True
    if ecosystem in ("npm_and_yarn", "npm"):
        shipped = shipped_npm_packages(package_json, yarn_lock)
        return any(name in shipped for name in dependency_names)
    return False


# ---- GitHub -----------------------------------------------------------------------------------------------------

def ensure_labels(repo):
    existing = {l["name"] for l in json.loads(gh("label", "list", "--repo", repo, "--limit", "200", "--json", "name"))}
    for name, (color, description) in LABELS.items():
        if name not in existing:
            gh("label", "create", name, "--repo", repo, "--color", color, "--description", description)


def comment_once(repo, number, marker, text):
    bodies = json.loads(gh("pr", "view", str(number), "--repo", repo, "--json", "comments"))["comments"]
    if not any(marker in c["body"] for c in bodies):
        gh("pr", "comment", str(number), "--repo", repo, "--body", "%s\n\n<!-- %s -->" % (text, marker))


def hold_marker(why):
    """One comment per reason: an update held again for another reason says so."""
    return "dependency-updates:hold:" + re.sub(r"[^a-z0-9]+", "-", why.lower()).strip("-")


def hold(repo, number, why):
    gh("pr", "edit", str(number), "--repo", repo, "--add-label", REVIEW, "--remove-label", AUTOMATIC, check=False)
    gh("pr", "merge", str(number), "--repo", repo, "--disable-auto", check=False)
    comment_once(repo, number, hold_marker(why), "Held for review: %s Claude Code reviews it, fixes what "
                 "breaks and merges it in a normal task (docs/development.md#dependency-updates)." % why)


def sort():
    repo, number = os.environ["REPO"], os.environ["PR"]
    update_type, group = os.environ.get("UPDATE_TYPE", ""), os.environ.get("GROUP", "")
    ecosystem = os.environ.get("ECOSYSTEM", "")
    names = [d["dependencyName"] for d in json.loads(os.environ.get("DEPENDENCIES") or "[]")]
    ensure_labels(repo)
    if not update_type or not group:
        commits = json.loads(gh("api", "repos/%s/pulls/%s/commits" % (repo, number), "--jq", "[.[].commit.message]"))
        update_type = update_type or size_from_text("\n".join(commits))
        group = group or group_from_text("\n".join(commits))

    labels = []
    if is_security(group):
        labels.append(SECURITY)
        package_json = open("package.json", encoding="utf-8").read() if os.path.exists("package.json") else "{}"
        yarn_lock = open("yarn.lock", encoding="utf-8").read() if os.path.exists("yarn.lock") else ""
        if ships(ecosystem, names, package_json, yarn_lock):
            labels.append(FOR_ADMINS)

    current = {l["name"] for l in json.loads(gh("pr", "view", number, "--repo", repo, "--json", "labels"))["labels"]}
    if REVIEW in current:
        print("#%s is already held for review" % number)
    elif automatic(update_type):
        labels.append(AUTOMATIC)
    else:
        labels.append(REVIEW)

    if labels:
        gh("pr", "edit", number, "--repo", repo, "--add-label", ",".join(labels))
    if REVIEW in labels:
        hold(repo, number, "a %s update (%s)." % (update_type.replace("version-update:semver-", "") or "differently "
                                                   "versioned", ", ".join(names) or "see the description"))
    print("#%s: %s %s, group %s, %s -> %s" % (number, ecosystem, update_type, group or "none", ", ".join(names), labels))


def build_state(repo, number):
    """The pull request's build check: SUCCESS, one of FAILED, or "" while it hasn't finished. It can have several runs
    (an older one from before build numbers, one started again): one that passed is enough, and one still running is
    waited for."""
    checks = json.loads(gh("pr", "checks", str(number), "--repo", repo, "--json", "name,state", check=False) or "[]")
    return decide_build([c["state"] for c in checks if c["name"] == "build"])


def decide_build(states):
    if "SUCCESS" in states:
        return "SUCCESS"
    if not states or any(state not in FAILED for state in states):
        return ""
    return states[0]


def wait_for_build(repo, number, minutes=45):
    deadline = time.time() + minutes * 60
    while True:
        state = build_state(repo, number)
        if state or time.time() > deadline:
            return state
        time.sleep(30)


def update():
    repo = os.environ["REPO"]
    ensure_labels(repo)
    # An update that merged or was held makes way for the next one in the same run.
    for _ in range(10):
        if not update_next(repo):
            return


def update_next(repo):
    """Moves the oldest automatic update on; True when it merged or was held, so the next one can go."""
    waiting = json.loads(gh("pr", "list", "--repo", repo, "--base", "dev", "--state", "open", "--label", AUTOMATIC,
                            "--json", "number,labels,author,createdAt"))
    waiting = [p for p in waiting if p["author"]["login"] in ("app/dependabot", "dependabot[bot]", "dependabot")
               and REVIEW not in {l["name"] for l in p["labels"]}]
    if not waiting:
        print("No automatic update is waiting.")
        return False
    number = sorted(waiting, key=lambda p: p["createdAt"])[0]["number"]
    print("Next automatic update: #%d; %d waiting" % (number, len(waiting)))

    # Dependabot's own commit runs build like any pull request; this workflow only waits for it, since GitHub starts no
    # workflow when a merge made with this workflow's token lands, and the hourly run can come late or not at all.
    state = wait_for_build(repo, number)
    if state in FAILED:
        hold(repo, number, "build didn't pass on it (%s)." % state.lower().replace("_", " "))
        return True
    if state == "SUCCESS" and subprocess.run(["gh", "pr", "merge", str(number), "--repo", repo, "--merge"]).returncode == 0:
        print("#%d: build passed, merged into dev" % number)
        return True
    gh("pr", "merge", str(number), "--repo", repo, "--auto", "--merge")
    print("#%d: merges on its own once build passes" % number)
    return False


# ---- self-test --------------------------------------------------------------------------------------------------

LOCK = '''# yarn lockfile v1


"app-dep@^1.0.0":
  version "1.2.0"
  dependencies:
    deep-dep "^2.0.0"

deep-dep@^2.0.0, deep-dep@^2.1.0:
  version "2.1.0"

tool@3.0.0:
  version "3.0.0"
  dependencies:
    tool-dep "~1.0.0"

tool-dep@~1.0.0:
  version "1.0.4"
'''


GROUPED = """Bump fast-uri

Bumps the npm_and_yarn group with 2 updates in the / directory: [fast-uri](x) and [ajv](y).


Updates `fast-uri` from 3.1.8 to 3.1.9
- [Release notes](x)

Updates `ajv` from 8.17.1 to 8.18.0
"""


def self_test():
    package = json.dumps({"dependencies": {"app-dep": "^1.0.0"}, "devDependencies": {"tool": "3.0.0"}})
    checks = [
        (automatic("version-update:semver-patch") and automatic("version-update:semver-minor"), "patch and minor merge"),
        (not automatic("version-update:semver-major") and not automatic(""), "major and unsized updates wait"),
        (is_security("frontend-security") and is_security("npm_and_yarn") and not is_security("frontend"),
         "security groups are recognised"),
        (shipped_npm_packages(package, LOCK) == {"app-dep", "deep-dep"}, "the web app's packages, followed through"),
        (ships("npm_and_yarn", ["deep-dep"], package, LOCK), "a package the app pulls in ships"),
        (not ships("npm_and_yarn", ["tool-dep"], package, LOCK), "a build tool's package doesn't ship"),
        (ships("nuget", ["NLog"]) and ships("docker", ["dotnet/aspnet"]) and not ships("github_actions", ["x"]),
         "backend and images ship, workflow actions don't"),
        (size_from_text(GROUPED) == "version-update:semver-minor", "a grouped update is sized by its biggest change"),
        (size_from_text("Updates `fast-uri` from 3.1.8 to 3.1.9\n") == "version-update:semver-patch", "a patch update"),
        (size_from_text("Bump actions/checkout from 4 to 5\n") == "version-update:semver-major", "a major update"),
        (size_from_text("Bump node from 20-bookworm-slim to 24-bookworm-slim") == "version-update:semver-major",
         "an image tag"),
        (size_from_text("nothing to size") == "", "no versions, no size"),
        (group_from_text(GROUPED) == "npm_and_yarn" and group_from_text("Bump x") == "", "the group from the message"),
        (hold_marker("build failed on it.") != hold_marker("it conflicts with dev in a.cs.")
         and hold_marker("build failed on it.") == hold_marker("Build failed on it"), "a hold comment per reason"),
        (decide_build(["FAILURE", "SUCCESS"]) == "SUCCESS", "one passing build is enough"),
        (decide_build(["FAILURE", "IN_PROGRESS"]) == "" and decide_build([]) == "", "a build still running is waited for"),
        (decide_build(["CANCELLED", "FAILURE"]) == "CANCELLED", "every build failing holds the update"),
    ]
    failed = [what for ok, what in checks if not ok]
    for what in failed:
        print("self-test failed: " + what)
    print("self-test: %d of %d passed" % (len(checks) - len(failed), len(checks)))
    return 1 if failed else 0


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "--self-test":
        sys.exit(self_test())
    if command == "sort":
        sort()
    elif command == "update":
        update()
    else:
        print(__doc__)
        sys.exit(1)
