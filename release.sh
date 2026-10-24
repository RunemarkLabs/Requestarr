#!/usr/bin/env bash
# The release flow (docs/development.md#release-flow): every pull request merges to dev; qa moves only on the
# owner's word; a fix found in QA goes into qa and is merged back into dev in the same task.
#
#   ./release.sh drift [<dev ref>] [<qa ref>]
#         fails if qa has a commit dev doesn't (defaults: origin/dev and origin/qa, fetched first)
#   ./release.sh promote-qa <version | commit> "<who asked, and their words>"
#         moves qa to exactly that point of dev: a pull request into qa that records who asked, the build check,
#         then a fast-forward of qa to that commit (nothing newer, no merge commit)
#   ./release.sh merge-back
#         after a fix merged into qa: a pull request that merges qa into dev, merged once the build check passes
#   ./release.sh check-changelog <base> <head> <target branch> [<pull request head>]
#         the version rules CI's Changelog job applies to a pull request
#
# Needs git, and the GitHub CLI (gh) for promote-qa and merge-back.
set -euo pipefail

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

drift() {
    local dev="${1:-}" qa="${2:-}"
    if [ -z "$dev" ]; then
        git fetch -q origin dev qa
        dev=origin/dev
        qa=origin/qa
    fi
    qa="${qa:-origin/qa}"

    local missing
    missing=$(git log --format='  %h %s' "$dev..$qa")
    if [ -n "$missing" ]; then
        echo "DRIFT: $qa has $(echo "$missing" | wc -l | tr -d ' ') commit(s) that $dev doesn't:"
        echo "$missing"
        echo "Merge qa back into dev (./release.sh merge-back) so dev has every fix made in QA."
        return 1
    fi
    echo "No drift: every commit in $qa ($(git rev-parse --short "$qa")) is in $dev ($(git rev-parse --short "$dev"))."
}

check_changelog() {
    local base="$1" head="$2" target="$3" pr_head="${4:-$2}"

    if ! git diff --name-only "$base" "$head" | grep -qx 'CHANGELOG.md'; then
        echo "::error::Add a section for this change at the top of CHANGELOG.md (see docs/development.md#changelog)."
        return 1
    fi

    local new old
    new=$(top_version "$head")
    old=$(top_version "$base")
    echo "Top version: $target ${old:-none}, this change ${new:-none}"
    [ -n "$new" ] || { echo "::error::CHANGELOG.md needs a '## x.y.z' section at the top."; return 1; }
    if [ -n "$old" ] && ! higher "$new" "$old"; then
        echo "::error::The top version in CHANGELOG.md ($new) must be higher than $target's ($old); bump <AssemblyVersion> in src/Directory.Build.props to match."
        return 1
    fi

    # dev and qa never use the same version for different changes.
    case "$target" in
        dev) other=qa ;;
        qa) other=dev ;;
        *) return 0 ;;
    esac
    git rev-parse -q --verify "origin/$other" >/dev/null || { echo "No $other branch yet: nothing more to check."; return 0; }

    if [ "$target" = qa ] && git merge-base --is-ancestor "$pr_head" origin/dev; then
        echo "A promotion of dev's commits into qa: their versions are already checked."
        return 0
    fi

    local theirs
    theirs=$(top_version "origin/$other")
    if git merge-base --is-ancestor "origin/$other" "$head"; then
        # This change already contains everything on the other branch (a merge-back, say): its version may equal it.
        if [ -n "$theirs" ] && higher "$theirs" "$new"; then
            echo "::error::$new is lower than $other's $theirs, which this change contains."
            return 1
        fi
    elif [ -n "$theirs" ] && ! higher "$new" "$theirs"; then
        echo "::error::$other already has $theirs, so this change needs a version higher than both $target's and $other's (a fix into qa takes the next number above dev's)."
        return 1
    fi
    echo "Version $new is free on both dev and qa."
}

# The newest commit on dev's first-parent line whose CHANGELOG.md tops out at the version.
commit_for_version() {
    local wanted="$1" commit version
    for commit in $(git rev-list --first-parent origin/dev); do
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

promote_qa() {
    local what="${1:-}" who="${2:-}"
    [ -n "$what" ] && [ -n "$who" ] || die 'promote-qa <version | commit> "<who asked, and their words>"'
    git fetch -q origin dev qa

    local target
    if [[ "$what" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        target=$(commit_for_version "$what") || die "no commit on dev has version $what at the top of CHANGELOG.md"
    else
        target=$(git rev-parse -q --verify "$what^{commit}") || die "no commit '$what'"
    fi

    git merge-base --is-ancestor "$target" origin/dev || die "$what isn't on dev: only dev's commits are promoted to QA"
    [ "$(git rev-parse origin/qa)" != "$target" ] || die "qa is already at $what"
    git merge-base --is-ancestor origin/qa "$target" \
        || die "qa has commits that $what doesn't (an older version, or drift: ./release.sh drift), so qa can't move to it"

    local version short branch commits versions
    version=$(top_version "$target")
    short=$(git rev-parse --short "$target")
    branch="promote/qa-$version"
    commits=$(git log --format='- %h %s' "origin/qa..$target")
    versions=$(comm -23 <(all_versions "$target" | sort -u) <(all_versions origin/qa | sort -u) | sort -rV | sed 's/^/- /')

    echo "Promoting $version ($short) to QA. Commits moving to qa:"
    echo "$commits"
    git push -q origin "$target:refs/heads/$branch"
    gh pr create --base qa --head "$branch" --title "Promote $version to QA" --body "$(cat <<EOF
Moves \`qa\` to \`dev\` @ \`$short\` (version $version): exactly these commits, nothing newer. Merged as a fast-forward of \`qa\` once the build check passes.

Requested by: $who

### Versions now in QA
$versions

### Commits
$commits
EOF
)"
    echo "Waiting for the build check…"
    sleep 15
    gh pr checks "$branch" --watch --required --interval 30 || die "the build check failed; qa is unchanged"

    git push origin "$target:refs/heads/qa"
    sleep 5
    echo "Pull request: $(gh pr view "$branch" --json url,state --jq '.url + " (" + .state + ")"')"
    git push -q origin --delete "$branch" || true
    git fetch -q origin qa
    echo "QA is now at $version ($(git rev-parse --short origin/qa)). New in QA:"
    echo "$versions"
    echo "Next: .\\docker\\dev.ps1 reset-qa (or docker/dev.sh reset-qa) so the QA stack runs qa."
}

merge_back() {
    git fetch -q origin dev qa
    if git merge-base --is-ancestor origin/qa origin/dev; then
        echo "dev already has everything in qa: nothing to merge back."
        return 0
    fi

    local version
    version=$(top_version origin/qa)
    if gh pr create --base dev --head qa --title "Merge QA fix $version back into dev" \
        --body "Merges \`qa\` (version $version) into \`dev\`, so dev has every fix made in QA and the drift check passes."; then
        :
    fi
    if [ "$(gh pr view qa --json mergeable --jq .mergeable)" = "CONFLICTING" ]; then
        cat <<EOF
qa and dev conflict (usually CHANGELOG.md and the version). Resolve on a branch:
  git switch -c merge-back/qa-$version origin/dev && git merge origin/qa
  CHANGELOG.md: keep every section, highest version on top. src/Directory.Build.props: the higher version.
  git commit, push, then: gh pr create --base dev --title "Merge QA fix $version back into dev"
EOF
        return 1
    fi

    echo "Waiting for the build check…"
    sleep 15
    gh pr checks qa --watch --required --interval 30 || die "the build check failed; nothing merged"
    gh pr merge qa --merge
    drift
}

case "${1:-}" in
    drift) shift; drift "$@" ;;
    check-changelog) shift; check_changelog "$@" ;;
    promote-qa) shift; promote_qa "$@" ;;
    merge-back) merge_back ;;
    *)
        sed -n '2,14p' "$0"
        exit 1
        ;;
esac
