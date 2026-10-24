#!/usr/bin/env bash
# Local dev/test stack for Requestarr.
#
#   docker/dev.sh up                    build and start everything, wait for health checks, run check
#   docker/dev.sh check                 health and status checks for Requestarr and the test backends
#   docker/dev.sh down                  stop the stack (data kept)
#   docker/dev.sh reset-test-backends   wipe the Sonarr/Radarr test instances back to empty and restart them
#   docker/dev.sh reset-uat [--stopped] clean slate: remove Requestarr's Discord channels and user roles, wipe its
#                                       database and the test backends, then start it again so the bot recreates
#                                       #requestarr-admin and its panel (UAT ready). --stopped leaves Requestarr
#                                       stopped, for the live Discord test
#   docker/dev.sh init                  create docker/secrets.env from the template (run by `up`)
#   docker/dev.sh logs [service]        follow logs
#   docker/dev.sh stop-backend sonarr|radarr      stop only that test instance (Requestarr alerts after 3 minutes)
#   docker/dev.sh start-backend sonarr|radarr     start it again (Requestarr says it's back)
#   docker/dev.sh fake-download movie "<title>"   make a title in the test Radarr look downloaded, like a real import
#   docker/dev.sh fake-download tv "<title>" <n>  the same for season n in the test Sonarr
#   docker/dev.sh fast-votes on|off|status        dev only: removal votes in minutes (vote 5, answer 5, reminder at 3)
#
# On Windows, docker/dev.ps1 does the same from PowerShell. Secret values are read from docker/secrets.env
# and never printed.
set -euo pipefail

# On Git Bash, pwd -W gives a Windows path that docker understands.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && (pwd -W 2>/dev/null || pwd))"
SECRETS="$DIR/secrets.env"
TEMPLATE="$DIR/secrets.env.example"

# Git Bash on Windows rewrites container paths unless told not to.
export MSYS_NO_PATHCONV=1

compose() {
    docker compose -f "$DIR/compose.yaml" --env-file "$SECRETS" "$@"
}

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

load_secrets() {
    set -a
    # shellcheck disable=SC1090
    . "$SECRETS"
    set +a
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

check() {
    load_secrets
    local rq="http://localhost:${REQUESTARR_PORT:-9797}"
    local sonarr="http://localhost:${SONARR_TEST_PORT:-18989}"
    local radarr="http://localhost:${RADARR_TEST_PORT:-17878}"
    local ok=0

    echo "== Containers"
    compose ps --format 'table {{.Service}}\t{{.Status}}'
    echo
    echo "== Requestarr"
    printf '%-26s %s\n' "ping" "$(curl -s "$rq/ping" | tr -d ' \n')"
    status_line "system/status" "$rq/api/v1/system/status" "$REQUESTARR_API_KEY" "appName,instanceName,version,authentication" || ok=1
    status_line "config/host (provisioned)" "$rq/api/v1/config/host" "$REQUESTARR_API_KEY" "instanceName,authenticationMethod,authenticationRequired,username,logLevel,analyticsEnabled" || ok=1
    status_line "health" "$rq/api/v1/health" "$REQUESTARR_API_KEY" "source,type,message" || ok=1
    echo
    echo "== Test backends"
    status_line "sonarr-test system/status" "$sonarr/api/v3/system/status" "$SONARR_TEST_API_KEY" "appName,version,authentication" || ok=1
    status_line "sonarr-test rootfolder" "$sonarr/api/v3/rootfolder" "$SONARR_TEST_API_KEY" "path,accessible" || ok=1
    status_line "radarr-test system/status" "$radarr/api/v3/system/status" "$RADARR_TEST_API_KEY" "appName,version,authentication" || ok=1
    status_line "radarr-test rootfolder" "$radarr/api/v3/rootfolder" "$RADARR_TEST_API_KEY" "path,accessible" || ok=1
    echo
    if [ "$ok" = 0 ]; then echo "CHECK PASSED"; else echo "CHECK FAILED"; fi
    return "$ok"
}

reset_test_backends() {
    compose rm -sf sonarr-test radarr-test test-backends-init
    local project="requestarr-dev"
    docker volume rm "${project}_sonarr-test-config" "${project}_radarr-test-config" "${project}_test-media" 2>/dev/null || true
    compose up -d --wait sonarr-test radarr-test test-backends-init
    echo "Test backends reset to empty"
}

reset_uat() {
    load_secrets
    compose rm -sf requestarr
    python -I "$DIR/reset-discord.py"
    docker volume rm "requestarr-dev_requestarr-config" 2>/dev/null || true
    reset_test_backends

    if [ "${1:-}" = "--stopped" ]; then
        echo "UAT reset: everything is clean and Requestarr is stopped. Run docker/dev.sh up when done."
        return
    fi

    compose up -d --build --wait
    check
    echo "UAT reset: clean and UAT ready. The bot recreates #requestarr-admin and its panel on start."
}

backend_service() {
    case "${1:-}" in
        sonarr) echo sonarr-test ;;
        radarr) echo radarr-test ;;
        *) echo "say which test instance: sonarr or radarr" >&2; exit 1 ;;
    esac
}

# The dev stack's Requestarr container, and only if it is marked as the dev stack.
requestarr_container() {
    local name
    name=$(docker ps --filter "label=com.docker.compose.project=requestarr-dev" --filter "label=com.docker.compose.service=requestarr" --format '{{.Names}}')
    [ -n "$name" ] || { echo "the dev stack's Requestarr isn't running (docker/dev.sh up)" >&2; exit 1; }
    docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$name" | grep -qx 'REQUESTARR_DEV_STACK=true'         || { echo "$name isn't marked as the dev stack; fast votes stay off" >&2; exit 1; }
    echo "$name"
}

fast_votes() {
    local name
    case "${1:-}" in
        on)
            name=$(requestarr_container)
            docker exec "$name" sh -c 'touch /config/dev-fast-votes'
            echo "Fast votes ON (dev stack only): new removal votes close after 5 minutes; requesters have 5 minutes to answer, reminded at minute 3."
            echo "Requestarr picks it up within a minute and posts a \"Dev: fast votes\" notice in #requestarr-admin. Removals already open keep their times."
            echo "It stays on until docker/dev.sh fast-votes off, or reset-uat (which always starts with the real settings)."
            ;;
        off)
            name=$(requestarr_container)
            docker exec "$name" sh -c 'rm -f /config/dev-fast-votes'
            echo "Fast votes off: removal timings are back to the real settings. Requestarr posts a \"Dev: fast votes\" notice in #requestarr-admin within a minute."
            ;;
        status)
            name=$(requestarr_container)
            if docker exec "$name" test -e /config/dev-fast-votes; then echo "Fast votes: ON (test timings, minutes)"; else echo "Fast votes: off"; fi
            ;;
        *) echo "fast-votes on, off or status" >&2; exit 1 ;;
    esac
}

case "${1:-}" in
    init) init ;;
    up)
        init
        compose up -d --build --wait
        check
        ;;
    check) check ;;
    down) compose down ;;
    reset-test-backends) reset_test_backends ;;
    reset-uat) reset_uat "${2:-}" ;;
    logs) shift; compose logs -f "$@" ;;
    stop-backend)
        service=$(backend_service "${2:-}")
        compose stop "$service"
        echo "Stopped $service. Requestarr checks every minute: after 3 minutes down it posts an alert in #requestarr-admin, new requests of that type say they're paused, and the instance waits in /admin requests. After 30 minutes the alert offers Announce to users."
        ;;
    start-backend)
        service=$(backend_service "${2:-}")
        compose start "$service"
        echo "Started $service. Within a minute Requestarr posts that it's back (if it had alerted) and requests work again."
        ;;
    fake-download)
        shift
        load_secrets
        python -I "$DIR/fake-download.py" "$@"
        ;;
    fast-votes) fast_votes "${2:-}" ;;
    *)
        sed -n '2,23p' "$0"
        exit 1
        ;;
esac
