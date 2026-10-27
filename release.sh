#!/usr/bin/env bash
# The release flow (docs/development.md#release-flow): every pull request merges to dev; qa moves only on the
# owner's word ("promote to QA"), staging only on "QA passed"; a fix found in QA or staging goes into that branch and
# is merged back down (staging into qa, qa into dev) in the same task, so the branches never drift.
#
#   ./release.sh drift [<dev ref> <qa ref> [<staging ref>]]
#         fails if qa has a commit dev doesn't, or staging one qa doesn't (default: origin/dev, origin/qa, origin/staging)
#   ./release.sh promote-qa <version | commit> "<who asked, and their words>" [--dry-run]
#   ./release.sh promote-staging <version | commit> "<who asked, and their words>" [--owner-override "<the owner's words>"] [--dry-run]
#         moves qa (or staging) to exactly that point of dev (or qa): a pull request that records who asked, the build
#         check, then a fast-forward to that commit (nothing newer, no merge commit). Staging's push publishes its image
#         as the beta version (x.y.z-beta.N, docs/development.md#versions), which the pull request records.
#         Staging only moves when the QA gate is clear (qa-gate below), unless the owner overrides it in so many words.
#         --dry-run says what would move and which versions come out of it, and changes nothing.
#   ./release.sh next-version [dev | qa | staging]
#         the version the next pull request into that branch takes (default dev)
#   ./release.sh qa-gate [--json]
#         is QA's test checklist complete, with no item failing and no bug report from QA still open? Read from the QA
#         stack on this machine (docker/qa-gate.py); exit 0 when clear
#   ./release.sh merge-back
#         after a fix merged into staging or qa: pull requests that merge staging into qa and qa into dev, each merged
#         once the build check passes. The changelog and QA checklist keep every version of both, highest first.
#   ./release.sh check-changelog <base> <head> <target branch> [<pull request head> [<pull request branch>]]
#         the changelog rules CI's Changelog job applies to a pull request
#
# Needs git and Python 3, and the GitHub CLI (gh) for promote-* and merge-back.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"

die() {
    echo "release.sh: $*" >&2
    exit 1
}

# The top "## x.y.z" version of CHANGELOG.md at a commit.
top_version() {
    git show "$1:CHANGELOG.md" 2>/dev/null | grep -m1 -E '^## [0-9]+\.[0-9]+\.[0-9]+' | sed -E 's/^## +//; s/ .*//' || true
}

# Every "## x.y.z" version in CHANGELOG.md at a commit.
all_versions() {
    git show "$1:CHANGELOG.md" 2>/dev/null | grep -E '^## [0-9]+\.[0-9]+\.[0-9]+' | sed -E 's/^## +//; s/ .*//' || true
}

# True if version $1 is higher than version $2.
higher() {
    [ "$1" != "$2" ] && [ "$(printf '%s\n' "$1" "$2" | sort -V | tail -1)" = "$1" ]
}

has_ref() {
    git rev-parse -q --verify "$1^{commit}" >/dev/null
}

# {"dev": [...], "qa": [...], "staging": [...]}: each branch's versions, newest first, for .github/version_rules.py.
# Optional overrides: name=ref (the pull request's base for its target, or what a promotion would move a branch to).
branch_versions() {
    local name ref override out="{"
    for name in dev qa staging; do
        ref="origin/$name"
        for override in "$@"; do
            [ "${override%%=*}" = "$name" ] && ref="${override#*=}"
        done
        has_ref "$ref" || continue
        out+="\"$name\": [$(all_versions "$ref" | sed 's/.*/"&"/' | paste -sd, -)],"
    done
    echo "${out%,}}"
}

# The first Python that really runs (a stub can exit 0 without running anything).
python() {
    local candidate
    for candidate in python3 python; do
        if [ "$(command "$candidate" -c 'print(42)' 2>/dev/null)" = 42 ]; then
            command "$candidate" "$@"
            return
        fi
    done
    die "Python 3 is needed for the changelog check"
}

# Fails if $2 (the branch above) has a commit $1 (the branch below) doesn't.
drift_pair() {
    local lower="$1" upper="$2" missing
    missing=$(git log --format='  %h %s' "$lower..$upper")
    if [ -n "$missing" ]; then
        echo "DRIFT: $upper has $(echo "$missing" | wc -l | tr -d ' ') commit(s) that $lower doesn't:"
        echo "$missing"
        return 1
    fi
    echo "No drift: every commit in $upper ($(git rev-parse --short "$upper")) is in $lower ($(git rev-parse --short "$lower"))."
}

drift() {
    local dev="${1:-}" qa="${2:-}" staging="${3:-}"
    if [ -z "$dev" ]; then
        git fetch -q origin dev qa
        git fetch -q origin staging 2>/dev/null || true
        dev=origin/dev
        qa=origin/qa
        has_ref origin/staging && staging=origin/staging
    fi

    local failed=0
    drift_pair "$dev" "$qa" || failed=1
    if [ -n "$staging" ]; then
        drift_pair "$qa" "$staging" || failed=1
    fi
    if [ "$failed" = 1 ]; then
        echo "Merge the fix back down (./release.sh merge-back) so every branch below has every fix made above it."
    fi
    return "$failed"
}

# Where a branch sits in the flow; a merge-back goes from a higher one into a lower one.
rank() {
    case "$1" in
        dev) echo 1 ;;
        qa) echo 2 ;;
        staging) echo 3 ;;
        main) echo 4 ;;
        *) echo 0 ;;
    esac
}

check_changelog() {
    local base="$1" head="$2" target="$3" pr_head="${4:-$2}" pr_branch="${5:-}"

    if ! git diff --name-only "$base" "$head" | grep -qx 'CHANGELOG.md'; then
        echo "::error::Add a section for this change at the top of CHANGELOG.md (see docs/development.md#changelog)."
        return 1
    fi

    # The words: two sections, plain English, no placeholders (.github/changelog_check.py says what it checks).
    local file
    file=$(mktemp)
    git show "$head:CHANGELOG.md" >"$file"
    python "$here/.github/changelog_check.py" "$file" || { rm -f "$file"; return 1; }

    # What to test: QA-CHECKLIST.md has this version's items, or just its heading when nothing can be tested by hand
    # (.github/checklist_check.py says what it checks).
    if ! git diff --name-only "$base" "$head" | grep -qx 'QA-CHECKLIST.md'; then
        echo "::error::Add this version's test items (or just its heading, when nothing can be tested by hand) at the top of QA-CHECKLIST.md (see docs/development.md#qa-checklist)."
        rm -f "$file"
        return 1
    fi
    local checklist
    checklist=$(mktemp)
    git show "$head:QA-CHECKLIST.md" >"$checklist"
    python "$here/.github/checklist_check.py" "$checklist" "$file" || { rm -f "$file" "$checklist"; return 1; }
    rm -f "$file" "$checklist"

    local new old
    new=$(top_version "$head")
    old=$(top_version "$base")
    echo "Top version: $target ${old:-none}, this change ${new:-none}"
    [ -n "$new" ] || { echo "::error::CHANGELOG.md needs a '## x.y.z' section at the top."; return 1; }

    # A merge-back (staging into qa, qa into dev) brings fixes down: no version of its own, but nothing may be lost.
    if [ "$(rank "${pr_branch%%/*}")" -gt "$(rank "$target")" ] || [[ "$pr_branch" == merge-back/* ]]; then
        local lost
        lost=$(comm -23 <( (all_versions "$base"; all_versions "$pr_head") | sort -u) <(all_versions "$head" | sort -u))
        if [ -n "$lost" ]; then
            echo "::error::A merge-back keeps every version from both branches; missing: $(echo "$lost" | tr '\n' ' ')"
            return 1
        fi
        echo "A merge-back from $pr_branch: every version from both branches is kept."
        return 0
    fi

    if [ -n "$old" ] && ! higher "$new" "$old"; then
        echo "::error::The top version in CHANGELOG.md ($new) must be higher than $target's ($old); bump <AssemblyVersion> in src/Directory.Build.props to match."
        return 1
    fi

    case "$target" in
        dev | qa | staging) ;;
        *) return 0 ;;
    esac

    if { [ "$target" = qa ] && has_ref origin/dev && git merge-base --is-ancestor "$pr_head" origin/dev; } ||
        { [ "$target" = staging ] && has_ref origin/qa && git merge-base --is-ancestor "$pr_head" origin/qa; }; then
        echo "A promotion of commits already on the branch below: their versions are already checked."
        return 0
    fi

    # The version rules (docs/development.md#versions): into dev the next patch on dev's line, or the next minor once
    # QA has that line; a fix into qa or staging the next free patch on that branch's own line; never a version
    # another branch has.
    local changelog problem
    changelog=$(mktemp)
    git show "$head:CHANGELOG.md" >"$changelog"
    problem=$(branch_versions "$target=$base" | python "$here/.github/version_rules.py" check "$target" "$new" "$changelog") || {
        rm -f "$changelog"
        echo "::error::$problem Bump <AssemblyVersion> in src/Directory.Build.props to match (docs/development.md#versions)."
        return 1
    }
    rm -f "$changelog"
    echo "Version $new follows the version rules for $target, and no other branch has it."
}

# The newest commit on a branch's first-parent line whose CHANGELOG.md tops out at the version.
commit_for_version() {
    local wanted="$1" branch="$2" commit version
    for commit in $(git rev-list --first-parent "$branch"); do
        version=$(top_version "$commit")
        if [ "$version" = "$wanted" ]; then
            echo "$commit"
            return 0
        fi
        if [ -n "$version" ] && higher "$wanted" "$version"; then
            break
        fi
    done
    return 1
}

# Waits for a pull request's checks, then requires the build check (what the branch rules require) to have passed.
# gh's --required doesn't see checks that repository rulesets require, so build is looked up by name.
wait_for_build() {
    sleep 15
    gh pr checks "$1" --watch --interval 30 >/dev/null 2>&1 || true
    local state
    state=$(gh pr checks "$1" --json name,state --jq '[.[] | select(.name == "build")][0].state')
    echo "build: ${state:-not reported}"
    [ "$state" = "SUCCESS" ]
}

# promote <from> <to> <version | commit> <who> [--owner-override <words>] [--dry-run]
promote() {
    local from="$1" to="$2" what="${3:-}" who="${4:-}" name dry=0 override="" arg
    [ $# -ge 4 ] && shift 4 || shift $#
    while [ $# -gt 0 ]; do
        arg="$1"
        shift
        case "$arg" in
            --dry-run) dry=1 ;;
            --owner-override)
                override="${1:-}"
                [ -n "$override" ] || die "--owner-override needs the owner's words"
                shift
                ;;
            *) die "unknown option $arg" ;;
        esac
    done
    name=$([ "$to" = qa ] && echo QA || echo staging)
    [ -n "$what" ] && [ -n "$who" ] || die "promote-$to <version | commit> \"<who asked, and their words>\""
    git fetch -q origin dev qa
    git fetch -q origin staging 2>/dev/null || true

    local target
    if [[ "$what" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        target=$(commit_for_version "$what" "origin/$from") || die "no commit on $from has version $what at the top of CHANGELOG.md"
    else
        target=$(git rev-parse -q --verify "$what^{commit}") || die "no commit '$what'"
    fi

    git merge-base --is-ancestor "$target" "origin/$from" || die "$what isn't on $from: only $from's commits are promoted to $name"
    [ "$(git rev-parse "origin/$to")" != "$target" ] || die "$to is already at $what"
    git merge-base --is-ancestor "origin/$to" "$target" \
        || die "$to has commits that $what doesn't (an older version, or drift: ./release.sh drift), so $to can't move to it"

    # Staging: only with QA's test checklist complete and no bug report from QA open, or on the owner's explicit word.
    local gate=""
    if [ "$to" = staging ]; then
        if [ -n "$override" ]; then
            gate="Owner override: $override (the QA gate was not checked or not clear)"
        else
            local verdict
            if verdict=$("$here/docker/dev.sh" qa-gate --json); then
                gate=$(printf '%s' "$verdict" | python -c 'import json,sys; v=json.load(sys.stdin); print("QA checklist: complete for %s (%d items), the last item passed by %s at %s; no bug report from QA open." % (", ".join(v["versions"]), v["total"], v["completedBy"], v["completedAt"]))')
            elif [ "$dry" = 1 ]; then
                gate="QA gate: blocked (a real promotion stops here unless the owner overrides it)."
            else
                "$here/docker/dev.sh" qa-gate || true
                die "promotion to staging is blocked: QA's test checklist isn't complete, an item is failing, or a bug report from QA is still open. Only the owner can override it: promote-staging <version> \"<who>\" --owner-override \"<the owner's words>\""
            fi
        fi
    fi

    local version short branch commits versions published="" next=""
    version=$(top_version "$target")
    short=$(git rev-parse --short "$target")
    branch="promote/$to-$version"
    commits=$(git log --format='- %h %s' "origin/$to..$target")
    versions=$(comm -23 <(all_versions "$target" | sort -u) <(all_versions "origin/$to" | sort -u) | sort -rV | sed 's/^/- /')
    if [ "$to" = staging ]; then
        # Each publish to staging is recorded as a v<x.y.z>-beta.<n> tag on its commit; n counts the line's publishes.
        published=$(git ls-remote --tags origin | python "$here/.github/version_rules.py" beta "$version" "$(git rev-parse "$target")")
    else
        next=$(branch_versions "qa=$target" | python "$here/.github/version_rules.py" next dev)
    fi

    echo "Promoting $version ($short) to $name. Commits moving to $to:"
    echo "$commits"
    echo "Versions now in $name:"
    echo "$versions"
    if [ -n "$published" ]; then
        echo "Published to staging as $published (image tags staging and $published; the commit is tagged v$published)."
    else
        echo "QA gets $version as it is; the next pull request into dev starts the next minor: $next."
    fi
    [ -z "$gate" ] || echo "$gate"
    if [ "$dry" = 1 ]; then
        echo "Dry run: nothing pushed, no pull request opened; $to stays at $(top_version "origin/$to") ($(git rev-parse --short "origin/$to"))."
        return 0
    fi
    git push -q origin "$target:refs/heads/$branch"
    local url
    url=$(gh pr create --base "$to" --head "$branch" --title "Promote $version to $name" --body "$(cat <<EOF
Moves \`$to\` to \`$from\` @ \`$short\` (version $version): exactly these commits, nothing newer. Merged as a fast-forward of \`$to\` once the build check passes.

Requested by: $who
${gate:+
$gate
}${published:+
Published to staging as: $published
}
### Versions now in $name
$versions

### Commits
$commits
EOF
)")
    echo "$url"
    echo "Waiting for the build check…"
    wait_for_build "$url" || die "the build check didn't pass; $to is unchanged"

    git push origin "$target:refs/heads/$to"
    sleep 5
    echo "Pull request: $url ($(gh pr view "$url" --json state --jq .state))"
    git push -q origin --delete "$branch" || true
    git fetch -q origin "$to"
    echo "$name is now at $version ($(git rev-parse --short "origin/$to")). New in $name:"
    echo "$versions"
    if [ "$to" = qa ]; then
        echo "Next: .\\docker\\dev.ps1 reset-qa (or docker/dev.sh reset-qa) so the QA stack runs qa."
    else
        echo "Next: the Image workflow publishes ghcr.io/runemarklabs/requestarr:staging and :$published and tags the commit v$published (gh run list --workflow image.yml);"
        echo "      .\\docker\\dev.ps1 up (or docker/dev.sh up) so QA drops the versions staging now has;"
        echo "      on the server: ./update.sh in the staging folder (docs/setup-guide.md, the beta environment)."
    fi
}

# resolve_merge_back <from> <to> <version>: when <from> and <to> conflict, the merge on a branch merge-back/<from>-<version>
# (made in a separate worktree, so the checkout here is untouched): CHANGELOG.md and QA-CHECKLIST.md keep every version
# of both, highest first, and the build version is the merged changelog's top. Prints the branch; fails, saying what to
# do, if anything else conflicts.
resolve_merge_back() {
    local from="$1" to="$2" version="$3" branch dir file conflicted others="" top
    branch="merge-back/$from-$version"
    dir=$(mktemp -d)
    git worktree add -q -b "$branch" "$dir" "origin/$to" >&2
    if ! git -C "$dir" merge -q --no-edit -m "Merge $from fix $version back into $to" "origin/$from" >/dev/null 2>&1; then
        conflicted=$(git -C "$dir" diff --name-only --diff-filter=U)
        for file in $conflicted; do
            case "$file" in
                CHANGELOG.md | QA-CHECKLIST.md)
                    git -C "$dir" show ":2:$file" >"$dir/.ours"
                    git -C "$dir" show ":3:$file" >"$dir/.theirs"
                    python "$here/.github/version_rules.py" merge "$dir/.ours" "$dir/.theirs" "$dir/$file" >&2 || others+=" $file"
                    rm -f "$dir/.ours" "$dir/.theirs"
                    ;;
                src/Directory.Build.props) git -C "$dir" checkout -q --ours src/Directory.Build.props ;;
                *) others+=" $file" ;;
            esac
        done
        if [ -n "$others" ]; then
            git -C "$dir" merge --abort
            git worktree remove --force "$dir"
            git branch -q -D "$branch"
            cat >&2 <<EOF
$from and $to conflict in:$others. Resolve on a branch:
  git switch -c $branch origin/$to && git merge origin/$from
  CHANGELOG.md and QA-CHECKLIST.md: python3 .github/version_rules.py merge <ours> <theirs> <file> (every version, highest first).
  src/Directory.Build.props: the changelog's top version.
  git commit, push, then: gh pr create --base $to --title "Merge $from fix $version back into $to"
EOF
            return 1
        fi
        top=$(grep -m1 -E '^## [0-9]+\.[0-9]+\.[0-9]+' "$dir/CHANGELOG.md" | sed -E 's/^## +//; s/ .*//')
        sed -i -E "s|<AssemblyVersion>[^<]*</AssemblyVersion>|<AssemblyVersion>$top.*</AssemblyVersion>|" "$dir/src/Directory.Build.props"
        git -C "$dir" add CHANGELOG.md QA-CHECKLIST.md src/Directory.Build.props
        git -C "$dir" commit -q --no-edit >&2
    fi
    git -C "$dir" push -q origin "$branch" >&2
    git worktree remove --force "$dir"
    echo "$branch"
}

# merge_into <from> <to>: a pull request that merges branch <from> into <to>, merged once the build check passes.
merge_into() {
    local from="$1" to="$2" version url head="$1"
    version=$(top_version "origin/$from")
    if ! git merge-tree --write-tree --no-messages "origin/$to" "origin/$from" >/dev/null 2>&1; then
        echo "$from and $to conflict; merging them on a branch, with every version of both kept, highest first."
        head=$(resolve_merge_back "$from" "$to" "$version") || return 1
    fi
    url=$(gh pr create --base "$to" --head "$head" --title "Merge $from fix $version back into $to" \
        --body "Merges \`$from\` (version $version) into \`$to\`, so $to has every fix made in $from and the drift check passes. The changelog and QA checklist keep every version of both, highest first.")
    echo "$url"
    sleep 10

    echo "Waiting for the build check…"
    wait_for_build "$url" || die "the build check didn't pass; nothing merged"
    # A merge commit (never squash): the branch below then contains the fix's own commits, which drift looks for.
    gh pr merge "$url" --merge
    git fetch -q origin "$to"
}

merge_back() {
    git fetch -q origin dev qa
    git fetch -q origin staging 2>/dev/null || true
    local merged=0

    if has_ref origin/staging && ! git merge-base --is-ancestor origin/staging origin/qa; then
        merge_into staging qa || return 1
        merged=1
    fi
    if ! git merge-base --is-ancestor origin/qa origin/dev; then
        merge_into qa dev || return 1
        merged=1
    fi
    [ "$merged" = 1 ] || echo "Every fix is already on the branches below: nothing to merge back."
    drift
}

case "${1:-}" in
    drift) shift; drift "$@" ;;
    check-changelog) shift; check_changelog "$@" ;;
    promote-qa) shift; promote dev qa "$@" ;;
    promote-staging) shift; promote qa staging "$@" ;;
    next-version)
        git fetch -q origin dev qa
        git fetch -q origin staging 2>/dev/null || true
        branch_versions | python "$here/.github/version_rules.py" next "${2:-dev}"
        ;;
    qa-gate) shift; "$here/docker/dev.sh" qa-gate "$@" ;;
    merge-back) merge_back ;;
    *)
        sed -n '2,26p' "$0"
        exit 1
        ;;
esac
