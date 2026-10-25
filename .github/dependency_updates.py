#!/usr/bin/env python3
"""Dependabot's pull requests into dev (docs/development.md#dependency-updates). The Dependency updates workflow runs it.

  sort      a new or changed update: label it "automatic update" (patch or minor) or "needs review" (major, or a
            change Dependabot can't size), plus "security" for a security fix and "for admins" when that fix is in
            something Requestarr ships (the backend, the base images, or a web app package the built app contains).
  update    the oldest open "automatic update": bring it up to date with dev, give it the next version and its
            changelog section, run build on that commit and merge it once build passes (hold it if build fails). One
            at a time, so two updates never take the same version; the next one goes when this one has merged.
  --self-test  checks the parts that decide, against built-in samples.

Needs git, the GitHub CLI (GH_TOKEN) and the repository checked out with full history.
"""
import base64
import datetime
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

SECURITY_LINE = ("- **Security update:** parts Requestarr is built from were updated to fix known security problems, "
                 "so update when you can.")

FAILED = ("failure", "timed_out", "startup_failure")

COMMIT_MESSAGE = "Version {version} and its changelog section for this dependency update"


def run(*args, check=True, capture=True):
    result = subprocess.run(args, check=False, text=True, capture_output=capture)
    if check and result.returncode != 0:
        sys.stderr.write(result.stderr or "")
        raise SystemExit("%s failed (exit %d)" % (" ".join(args), result.returncode))
    return (result.stdout or "").strip()


def gh(*args, check=True):
    return run("gh", *args, check=check)


def git(*args, check=True):
    return run("git", *args, check=check)


def gh_api(method, path, payload):
    # Payload on stdin: a blob of yarn.lock is far longer than one command-line argument may be.
    result = subprocess.run(["gh", "api", "-X", method, path, "--input", "-"], input=json.dumps(payload), text=True,
                            capture_output=True)
    if result.returncode != 0:
        sys.stderr.write(result.stderr or result.stdout or "")
        return None
    return json.loads(result.stdout)


def publish(repo, branch):
    """Makes the local HEAD commit again through GitHub's API and moves the branch to it; False if GitHub refused.

    A commit this workflow pushes with git is unsigned, and the repository's rules want an extra approval for those.
    One made through the API with the workflow's token is signed by GitHub, so the update can merge on its own."""
    parents = git("rev-list", "--parents", "-n", "1", "HEAD").split()[1:]
    # Start from the parent closest to the result (dev, when dev was merged in), so only a few files are sent.
    base = min(parents, key=lambda p: len(git("diff", "--name-only", "--no-renames", p, "HEAD").splitlines()))
    entries = []
    for line in git("diff", "--name-status", "--no-renames", base, "HEAD").splitlines():
        status, path = line.split("\t", 1)
        if status == "D":
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": None})
            continue
        mode = git("ls-tree", "HEAD", "--", path).split()[0]
        content = base64.b64encode(subprocess.run(["git", "show", "HEAD:" + path], capture_output=True).stdout)
        blob = gh_api("POST", "repos/%s/git/blobs" % repo, {"content": content.decode(), "encoding": "base64"})
        if blob is None:
            return False
        entries.append({"path": path, "mode": mode, "type": "blob", "sha": blob["sha"]})
    tree = gh_api("POST", "repos/%s/git/trees" % repo, {"base_tree": git("rev-parse", base + "^{tree}"),
                                                         "tree": entries})
    if tree is None or tree["sha"] != git("rev-parse", "HEAD^{tree}"):
        sys.stderr.write("the tree GitHub built differs from the local one\n")
        return False
    commit = gh_api("POST", "repos/%s/git/commits" % repo, {"message": git("log", "-1", "--format=%B", "HEAD"),
                                                            "tree": tree["sha"], "parents": parents})
    if commit is None or gh_api("PATCH", "repos/%s/git/refs/heads/%s" % (repo, branch),
                                {"sha": commit["sha"], "force": False}) is None:
        return False
    git("fetch", "--quiet", "origin", branch)
    git("reset", "--quiet", "--hard", commit["sha"])
    return True


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


# ---- versions ---------------------------------------------------------------------------------------------------

def top_version(changelog):
    match = re.search(r"^## (\d+)\.(\d+)\.(\d+)", changelog, re.MULTILINE)
    return tuple(int(x) for x in match.groups()) if match else (0, 0, 0)


def next_version(*changelogs):
    major, minor, patch = max(top_version(c) for c in changelogs)
    return (major, minor, patch + 1)


def with_section(changelog, version, day, security_line):
    """The changelog with a new version section on top (above the first "## x.y.z")."""
    heading = "## %d.%d.%d (%s)\n" % (version + (day,))
    body = "\n### For admins\n%s\n" % security_line if security_line else ""
    match = re.search(r"^## \d+\.\d+\.\d+", changelog, re.MULTILINE)
    at = match.start() if match else len(changelog)
    return changelog[:at] + heading + body + "\n" + changelog[at:]


def with_assembly_version(props, version):
    return re.sub(r"<AssemblyVersion>[^<]*</AssemblyVersion>", "<AssemblyVersion>%d.%d.%d.*</AssemblyVersion>" % version,
                  props, count=1)


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


def hold(repo, number, why):
    gh("pr", "edit", str(number), "--repo", repo, "--add-label", REVIEW, "--remove-label", AUTOMATIC, check=False)
    gh("pr", "merge", str(number), "--repo", repo, "--disable-auto", check=False)
    comment_once(repo, number, "dependency-updates:hold", "Held for review: %s Claude Code reviews it, fixes what "
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


def dispatched_builds(repo, sha):
    # Only the build this workflow starts counts: its commit can also queue pull request runs that wait for an
    # approval nobody gives ("action_required"), and those aren't failures.
    runs = json.loads(gh("run", "list", "--repo", repo, "--workflow", "build.yml", "--commit", sha,
                         "--json", "status,conclusion,event,databaseId"))
    return [r for r in runs if r["event"] == "workflow_dispatch"]


def wait_for_build(repo, sha, minutes=45):
    deadline = time.time() + minutes * 60
    while True:
        runs = dispatched_builds(repo, sha)
        if (runs and all(r["status"] == "completed" for r in runs)) or time.time() > deadline:
            return runs
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
    waiting =json.loads(gh("pr", "list", "--repo", repo, "--base", "dev", "--state", "open", "--label", AUTOMATIC,
                            "--json", "number,headRefName,labels,author,createdAt,isDraft"))
    waiting = [p for p in waiting if p["author"]["login"] in ("app/dependabot", "dependabot[bot]", "dependabot")
               and REVIEW not in {l["name"] for l in p["labels"]}]
    if not waiting:
        print("No automatic update is waiting.")
        return False
    pr =sorted(waiting, key=lambda p: p["createdAt"])[0]
    number, branch = pr["number"], pr["headRefName"]
    labels = {l["name"] for l in pr["labels"]}
    print("Next automatic update: #%d (%s); %d waiting" % (number, branch, len(waiting)))

    git("fetch", "--quiet", "origin", "dev", "qa", "staging", branch)
    git("checkout", "--quiet", "-B", "update", "origin/" + branch)
    tops = {b: git("show", "origin/%s:CHANGELOG.md" % b) for b in ("dev", "qa", "staging")}
    current = open("CHANGELOG.md", encoding="utf-8").read()
    up_to_date = run("git", "merge-base", "--is-ancestor", "origin/dev", "HEAD", check=False) is not None and \
        subprocess.run(["git", "merge-base", "--is-ancestor", "origin/dev", "HEAD"]).returncode == 0
    versioned = top_version(current) > max(top_version(t) for t in tops.values())

    if not (up_to_date and versioned):
        if not up_to_date:
            merged = subprocess.run(["git", "merge", "--no-edit", "--quiet", "origin/dev"], text=True, capture_output=True)
            if merged.returncode != 0:
                conflicted = git("diff", "--name-only", "--diff-filter=U").splitlines()
                others = [f for f in conflicted if f not in ("CHANGELOG.md", "src/Directory.Build.props")]
                if others:
                    git("merge", "--abort", check=False)
                    hold(repo, number, "it conflicts with dev in %s." % ", ".join(others))
                    return True
        # Dependabot never changes these two: start from dev's and add this update's version on top.
        git("checkout", "origin/dev", "--", "CHANGELOG.md", "src/Directory.Build.props")
        version = next_version(*tops.values())
        day = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
        line = SECURITY_LINE if FOR_ADMINS in labels else ""
        changelog = with_section(open("CHANGELOG.md", encoding="utf-8").read(), version, day, line)
        open("CHANGELOG.md", "w", encoding="utf-8", newline="\n").write(changelog)
        props = open("src/Directory.Build.props", encoding="utf-8").read()
        open("src/Directory.Build.props", "w", encoding="utf-8", newline="").write(with_assembly_version(props, version))
        git("add", "CHANGELOG.md", "src/Directory.Build.props")
        merging = subprocess.run(["git", "rev-parse", "-q", "--verify", "MERGE_HEAD"], capture_output=True).returncode == 0
        if merging or subprocess.run(["git", "diff", "--cached", "--quiet"]).returncode != 0:
            git("commit", "--quiet", "--no-verify", "-m", COMMIT_MESSAGE.format(version="%d.%d.%d" % version))
        if not publish(repo, branch):
            hold(repo, number, "its version and changelog section could not be added (GitHub refused the commit).")
            return True
        print("#%d: version %d.%d.%d added" % ((number,) + version))

    sha = git("rev-parse", "HEAD")
    if not dispatched_builds(repo, sha):
        # Commits made by this workflow don't start workflows, so build is started here on this commit.
        gh("workflow", "run", "build.yml", "--repo", repo, "--ref", branch, "-f", "base=dev")
        print("#%d: build started on %s" % (number, sha[:7]))
    # Wait for it here: GitHub starts no workflow when a build this workflow started finishes, nor when auto-merge
    # that it turned on merges, and the hourly run can come late or not at all.
    runs = wait_for_build(repo, sha)
    if any(r["conclusion"] in FAILED for r in runs) and not any(r["conclusion"] == "success" for r in runs):
        hold(repo, number, "build failed on it.")
        return True
    if any(r["conclusion"] == "success" for r in runs):
        if subprocess.run(["gh", "pr", "merge", str(number), "--repo", repo, "--merge"]).returncode == 0:
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
        (next_version("## 0.4.2 (x)", "## 0.3.4 (x)", "## 0.5.0 (x)") == (0, 5, 1), "next version is above all three"),
        (with_section("# Changelog\n\nintro\n\n## 0.4.2 (2026-10-25)\n", (0, 4, 3), "2026-10-26", "")
         == "# Changelog\n\nintro\n\n## 0.4.3 (2026-10-26)\n\n## 0.4.2 (2026-10-25)\n", "a heading-only version"),
        ("### For admins\n- **Security update:**" in with_section("## 0.1.0 (x)\n", (0, 1, 1), "d", SECURITY_LINE),
         "a security fix that ships tells admins"),
        (size_from_text(GROUPED) == "version-update:semver-minor", "a grouped update is sized by its biggest change"),
        (size_from_text("Updates `fast-uri` from 3.1.8 to 3.1.9\n") == "version-update:semver-patch", "a patch update"),
        (size_from_text("Bump actions/checkout from 4 to 5\n") == "version-update:semver-major", "a major update"),
        (size_from_text("Bump node from 20-bookworm-slim to 24-bookworm-slim") == "version-update:semver-major",
         "an image tag"),
        (size_from_text("nothing to size") == "", "no versions, no size"),
        (group_from_text(GROUPED) == "npm_and_yarn" and group_from_text("Bump x") == "", "the group from the message"),
        (with_assembly_version("<AssemblyVersion>0.4.2.*</AssemblyVersion>", (0, 4, 3))
         == "<AssemblyVersion>0.4.3.*</AssemblyVersion>", "the assembly version follows"),
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
