#!/usr/bin/env bash
# Local test stacks for Requestarr: QA (the owner's manual QA, the default) and dev (Claude Code's automated
# testing), side by side. Add --stack dev to act on the dev stack; QA is used otherwise.
#
#   docker/dev.sh up [--ref <git ref>]            build Requestarr from the stack's branch (qa or dev; --ref for another
#                                                 commit), start everything, wait for health checks, run check
#   docker/dev.sh check                           health and status checks: Requestarr, its Discord bot, test backends
#   docker/dev.sh stacks                          both stacks side by side: branch and commit, version, ports, bot
#   docker/dev.sh down                            stop the stack (data kept)
#   docker/dev.sh reset-qa [--stopped]            clean slate for QA: remove Requestarr's channels and user roles from the
#                                                 QA server, wipe its database and test backends, then start it again from
#                                                 qa (fast votes off). --stopped leaves it stopped. reset-uat still works
#   docker/dev.sh reset-dev [--stopped]           the same for the dev stack and the dev server (for live tests: --stopped)
#   docker/dev.sh reset-test-backends             wipe the stack's Sonarr/Radarr test instances back to empty
#   docker/dev.sh init                            create docker/secrets.env from the template (run by `up`)
#   docker/dev.sh logs [service]                  follow logs
#   docker/dev.sh stop-backend sonarr|radarr      stop only that test instance (Requestarr alerts after 3 minutes)
#   docker/dev.sh start-backend sonarr|radarr     start it again (Requestarr says it's back)
#   docker/dev.sh fake-download movie "<title>"   make a title in the test Radarr look downloaded, like a real import
#   docker/dev.sh fake-download tv "<title>" <n>  the same for season n in the test Sonarr
#   docker/dev.sh fast-votes on|off|status        test timings for removal votes (vote 5 min, answer 5, reminder at 3)
#   docker/dev.sh qa-gate [--json]                 is the QA checklist complete with no bug report from QA open? (exit 0: clear)
#
# On Windows, docker/dev.ps1 does the same from PowerShell. Secret values are read from docker/secrets.env
# and never printed.
set -euo pipefail

# On Git Bash, pwd -W gives a Windows path that docker understands.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && (pwd -W 2>/dev/null || pwd))"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SECRETS="$DIR/secrets.env"
TEMPLATE="$DIR/secrets.env.example"

# Git Bash on Windows rewrites container paths unless told not to.
export MSYS_NO_PATHCONV=1

die() {
    echo "dev.sh: $*" >&2
    exit 1
}

# --stack and --ref can go anywhere; everything else is the command and its arguments.
STACK="${REQUESTARR_STACK:-qa}"
STACK_GIVEN=""
REF=""
ARGS=()
while [ $# -gt 0 ]; do
    case "$1" in
        --stack|-stack|-Stack) STACK="${2:-}"; STACK_GIVEN=1; shift 2 || die "--stack needs qa or dev" ;;
        --stack=*) STACK="${1#--stack=}"; STACK_GIVEN=1; shift ;;
        --ref|-ref|-Ref) REF="${2:-}"; shift 2 || die "--ref needs a branch or commit" ;;
        *) ARGS+=("$1"); shift ;;
    esac
done
set -- ${ARGS[@]+"${ARGS[@]}"}

random_hex() {
    od -An -tx1 -N16 /dev/urandom | tr -d ' \n'
}

init() {
    if [ -f "$SECRETS" ]; then
        echo "secrets.env already exists, leaving it as is"
        return
    fi

    while IFS= read -r line || [ -n "$line" ]; do
        if [[ "$line" == *=GENERATE ]]; then
            echo "${line%GENERATE}$(random_hex)"
        else
            echo "$line"
        fi
    done < "$TEMPLATE" > "$SECRETS"

    echo "Created docker/secrets.env with generated local test keys (gitignored)"
}

# Reads secrets.env and sets what the chosen stack uses: its Compose project, ports, and its own Discord bot.
prepare() {
    [ -f "$SECRETS" ] || die "docker/secrets.env is missing: run docker/dev.sh init (or up) first."
    set -a
    # shellcheck disable=SC1090
    . "$SECRETS"
    set +a

    case "$STACK" in
        qa)
            STACK_NAME="QA"
            STACK_REQUESTARR_PORT="${REQUESTARR_PORT:-29797}"
            STACK_SONARR_TEST_PORT="${SONARR_TEST_PORT:-28989}"
            STACK_RADARR_TEST_PORT="${RADARR_TEST_PORT:-27878}"
            STACK_DISCORD_BOT_TOKEN="${DISCORD_BOT_TOKEN:-}"
            STACK_DISCORD_APPLICATION_ID="${DISCORD_APPLICATION_ID:-}"
            STACK_DISCORD_GUILD_ID="${DISCORD_TEST_GUILD_ID:-}"
            STACK_DISCORD_ADMIN_ROLE_ID="${DISCORD_ADMIN_ROLE_ID:-}"
            STACK_GITHUB_ISSUES_TOKEN="${GITHUB_ISSUES_TOKEN:-}"
            ;;
        dev)
            STACK_NAME="Dev"
            STACK_REQUESTARR_PORT="${DEV_REQUESTARR_PORT:-39797}"
            STACK_SONARR_TEST_PORT="${DEV_SONARR_TEST_PORT:-38989}"
            STACK_RADARR_TEST_PORT="${DEV_RADARR_TEST_PORT:-37878}"
            STACK_DISCORD_BOT_TOKEN="${DEV_DISCORD_BOT_TOKEN:-}"
            STACK_DISCORD_APPLICATION_ID="${DEV_DISCORD_APPLICATION_ID:-}"
            STACK_DISCORD_GUILD_ID="${DEV_DISCORD_GUILD_ID:-}"
            STACK_DISCORD_ADMIN_ROLE_ID="${DEV_DISCORD_ADMIN_ROLE_ID:-}"
            STACK_GITHUB_ISSUES_TOKEN="${DEV_GITHUB_ISSUES_TOKEN:-}"
            ;;
        *) die "--stack is qa or dev (got '$STACK')" ;;
    esac

    PROJECT="requestarr-$STACK"
    REQUESTARR_STACK="$STACK"
    # Set when the stack is built (build_image), the only time Requestarr's container is recreated.
    REQUESTARR_STAGED_VERSIONS=""
    export REQUESTARR_STACK STACK_REQUESTARR_PORT STACK_SONARR_TEST_PORT STACK_RADARR_TEST_PORT \
        STACK_DISCORD_BOT_TOKEN STACK_DISCORD_APPLICATION_ID STACK_DISCORD_GUILD_ID STACK_DISCORD_ADMIN_ROLE_ID \
        STACK_GITHUB_ISSUES_TOKEN REQUESTARR_STAGED_VERSIONS
}

# QA only: the versions staging has (what was last published for it, staged-versions.py), which QA's what's-new
# posts leave out (docs/development.md#version-posts). Empty for dev, or before anything is published for staging.
staged_versions() {
    [ "$STACK" = qa ] || return 0
    python -I "$DIR/staged-versions.py" || true
}

compose() {
    docker compose -p "$PROJECT" -f "$DIR/compose.yaml" --env-file "$SECRETS" "$@"
}

# Builds requestarr:<stack> from the stack's branch as it is on GitHub (origin/qa or origin/dev), or from --ref:
# committed files only, so what runs is exactly that commit. The image is labelled with the commit and version.
build_image() {
    local ref="$REF" sha version
    if [ -z "$ref" ]; then
        git -C "$REPO" fetch -q origin "$STACK" 2>/dev/null || echo "Couldn't fetch origin/$STACK; using the copy already here."
        ref="origin/$STACK"
    fi
    if [ "$STACK" = qa ]; then
        REQUESTARR_STAGED_VERSIONS=$(staged_versions)
        echo "Versions staging has (QA's what's new leaves them out): ${REQUESTARR_STAGED_VERSIONS:-none}"
    fi
    sha=$(git -C "$REPO" rev-parse --verify -q "$ref^{commit}") || die "no branch or commit '$ref' (git fetch first?)"
    version=$(git -C "$REPO" show "$sha:src/Directory.Build.props" | sed -nE 's/.*<AssemblyVersion>([0-9]+\.[0-9]+\.[0-9]+).*/\1/p')
    echo "Building requestarr:$STACK from $ref @ ${sha:0:7} (version $version)"
    git -C "$REPO" archive --format=tar "$sha" | docker build --quiet -t "requestarr:$STACK" -f docker/Dockerfile \
        --label "org.opencontainers.image.revision=$sha" --label "org.opencontainers.image.ref.name=$ref" \
        --label "org.opencontainers.image.version=$version" - >/dev/null || die "the image build failed"
}

status_line() {
    # name, url, header value (API key); prints HTTP code and selected JSON fields
    local name="$1" url="$2" key="$3" fields="$4"
    local body code
    body=$(curl -s -w '\n%{http_code}' -H "X-Api-Key: $key" "$url") || true
    code="${body##*$'\n'}"
    body="${body%$'\n'*}"
    printf '%-26s HTTP %s  %s\n' "$name" "$code" "$(echo "$body" | python -I -c "
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    print('(no JSON)'); sys.exit()
items = data if isinstance(data, list) else [data]
print('; '.join(', '.join(f'{f}={i.get(f)}' for f in sys.argv[1].split(',')) for i in items))
" "$fields")"
    [ "$code" = "200" ]
}

requestarr_name() {
    docker ps --filter "label=com.docker.compose.project=$PROJECT" --filter "label=com.docker.compose.service=requestarr" --format '{{.Names}}'
}

image_line() {
    local name
    name=$(requestarr_name)
    if [ -z "$name" ]; then echo "not running"; return; fi
    docker inspect --format '{{index .Config.Labels "org.opencontainers.image.ref.name"}} @ {{index .Config.Labels "org.opencontainers.image.revision"}} (version {{index .Config.Labels "org.opencontainers.image.version"}})' "$name" \
        | sed -E 's/@ ([0-9a-f]{7})[0-9a-f]+/@ \1/'
}

check() {
    local rq="http://localhost:$STACK_REQUESTARR_PORT"
    local sonarr="http://localhost:$STACK_SONARR_TEST_PORT"
    local radarr="http://localhost:$STACK_RADARR_TEST_PORT"
    local ok=0

    echo "== $STACK_NAME stack (Compose project $PROJECT)"
    printf '%-26s %s\n' "built from" "$(image_line)"
    echo
    echo "== Containers"
    compose ps --format 'table {{.Service}}\t{{.Status}}'
    echo
    echo "== Requestarr ($rq)"
    printf '%-26s %s\n' "ping" "$(curl -s "$rq/ping" | tr -d ' \n')"
    status_line "system/status" "$rq/api/v1/system/status" "$REQUESTARR_API_KEY" "appName,instanceName,version,authentication" || ok=1
    status_line "config/host (provisioned)" "$rq/api/v1/config/host" "$REQUESTARR_API_KEY" "instanceName,authenticationMethod,authenticationRequired,username,logLevel,analyticsEnabled" || ok=1
    status_line "health" "$rq/api/v1/health" "$REQUESTARR_API_KEY" "source,type,message" || ok=1
    printf '%-26s %s\n' "fast votes" "$(fast_votes_state)"
    echo
    echo "== Discord ($STACK_NAME bot and server)"
    python -I "$DIR/discord-status.py" "$PROJECT" || ok=1
    echo
    echo "== Test backends"
    status_line "sonarr-test system/status" "$sonarr/api/v3/system/status" "$SONARR_TEST_API_KEY" "appName,version,authentication" || ok=1
    status_line "sonarr-test rootfolder" "$sonarr/api/v3/rootfolder" "$SONARR_TEST_API_KEY" "path,accessible" || ok=1
    status_line "radarr-test system/status" "$radarr/api/v3/system/status" "$RADARR_TEST_API_KEY" "appName,version,authentication" || ok=1
    status_line "radarr-test rootfolder" "$radarr/api/v3/rootfolder" "$RADARR_TEST_API_KEY" "path,accessible" || ok=1
    echo
    if [ "$ok" = 0 ]; then echo "CHECK PASSED ($STACK_NAME stack)"; else echo "CHECK FAILED ($STACK_NAME stack)"; fi
    return "$ok"
}

stacks() {
    local s
    printf '%-6s %-44s %-28s %s\n' "STACK" "BUILT FROM" "REQUESTARR" "DISCORD BOT"
    for s in qa dev; do
        STACK="$s"
        prepare
        local name bot
        name=$(requestarr_name)
        bot="-"
        if [ -n "$name" ]; then
            bot=$(docker logs "$name" 2>&1 | grep -o "Discord bot ready in server '[^']*'" | tail -1 | sed "s/Discord bot ready in server/online in/")
            bot="${bot:-not connected}"
        fi
        printf '%-6s %-44s %-28s %s\n' "$s" "$(image_line)" "http://localhost:$STACK_REQUESTARR_PORT" "$bot"
    done
}

reset_test_backends() {
    compose rm -sf sonarr-test radarr-test test-backends-init
    docker volume rm "${PROJECT}_sonarr-test-config" "${PROJECT}_radarr-test-config" "${PROJECT}_test-media" 2>/dev/null || true
    compose up -d --wait sonarr-test radarr-test test-backends-init
    echo "Test backends reset to empty ($STACK_NAME stack)"
}

reset_stack() {
    compose rm -sf requestarr
    if [ -n "$STACK_DISCORD_BOT_TOKEN" ] && [ -n "$STACK_DISCORD_GUILD_ID" ]; then
        python -I "$DIR/reset-discord.py"
    fi
    docker volume rm "${PROJECT}_requestarr-config" 2>/dev/null || true
    reset_test_backends

    if [ "${1:-}" = "--stopped" ]; then
        echo "$STACK_NAME reset: everything is clean and Requestarr is stopped. Run docker/dev.sh --stack $STACK up when done."
        return
    fi

    build_image
    compose up -d --wait
    check
    echo "$STACK_NAME reset: clean and ready (fast votes off). The bot recreates #requestarr-admin and its panel on start."
}

backend_service() {
    case "${1:-}" in
        sonarr) echo sonarr-test ;;
        radarr) echo radarr-test ;;
        *) die "say which test instance: sonarr or radarr" ;;
    esac
}

# The stack's Requestarr container, and only if it is marked as a local test stack.
requestarr_container() {
    local name
    name=$(requestarr_name)
    [ -n "$name" ] || die "the $STACK_NAME stack's Requestarr isn't running (docker/dev.sh --stack $STACK up)"
    docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$name" | grep -qx 'REQUESTARR_DEV_STACK=true' \
        || die "$name isn't marked as a local test stack; fast votes stay off"
    echo "$name"
}

fast_votes_state() {
    local name
    name=$(requestarr_name)
    if [ -z "$name" ]; then echo "unknown (Requestarr not running)"; return; fi
    if docker exec "$name" test -e /config/dev-fast-votes 2>/dev/null; then echo "ON (test timings, minutes)"; else echo "off"; fi
}

fast_votes() {
    local name
    case "${1:-}" in
        on)
            name=$(requestarr_container)
            docker exec "$name" sh -c 'touch /config/dev-fast-votes'
            echo "Fast votes ON ($STACK_NAME stack): new removal votes close after 5 minutes; requesters have 5 minutes to answer, reminded at minute 3."
            echo "Requestarr picks it up within a minute and posts a \"Dev: fast votes\" notice in #requestarr-admin. Removals already open keep their times."
            echo "It stays on until fast-votes off, or a reset (which always starts with the real settings)."
            ;;
        off)
            name=$(requestarr_container)
            docker exec "$name" sh -c 'rm -f /config/dev-fast-votes'
            echo "Fast votes off: removal timings are back to the real settings. Requestarr posts a \"Dev: fast votes\" notice in #requestarr-admin within a minute."
            ;;
        status) echo "Fast votes ($STACK_NAME stack): $(fast_votes_state)" ;;
        *) die "fast-votes on, off or status" ;;
    esac
}

# reset-qa and reset-dev name their stack; refuse a --stack that says otherwise.
only_stack() {
    if [ -n "$STACK_GIVEN" ] && [ "$STACK" != "$1" ]; then die "$2 acts on the $1 stack (drop --stack $STACK)"; fi
    STACK="$1"
}

case "${1:-}" in
    init) init ;;
    up)
        init
        prepare
        build_image
        compose up -d --wait
        check
        ;;
    check) prepare; check ;;
    stacks) stacks ;;
    down) prepare; compose down ;;
    reset-test-backends) prepare; reset_test_backends ;;
    reset-qa|reset-uat) only_stack qa "$1"; prepare; reset_stack "${2:-}" ;;
    reset-dev) only_stack dev "$1"; prepare; reset_stack "${2:-}" ;;
    logs) shift; prepare; compose logs -f "$@" ;;
    stop-backend)
        prepare
        service=$(backend_service "${2:-}")
        compose stop "$service"
        echo "Stopped $service ($STACK_NAME stack). Requestarr checks every minute: after 3 minutes down it posts an alert in #requestarr-admin, new requests of that type say they're paused, and the instance waits in /admin requests. After 30 minutes the alert offers Announce to users."
        ;;
    start-backend)
        prepare
        service=$(backend_service "${2:-}")
        compose start "$service"
        echo "Started $service ($STACK_NAME stack). Within a minute Requestarr posts that it's back (if it had alerted) and requests work again."
        ;;
    fake-download)
        shift
        prepare
        REQUESTARR_COMPOSE_PROJECT="$PROJECT" RADARR_TEST_PORT="$STACK_RADARR_TEST_PORT" SONARR_TEST_PORT="$STACK_SONARR_TEST_PORT" \
            python -I "$DIR/fake-download.py" "$@"
        ;;
    fast-votes) prepare; fast_votes "${2:-}" ;;
    qa-gate)
        shift
        prepare
        QA_GATE_URL="http://localhost:$STACK_REQUESTARR_PORT" python -I "$DIR/qa-gate.py" "$@"
        ;;
    *)
        sed -n '2,25p' "$0"
        exit 1
        ;;
esac
