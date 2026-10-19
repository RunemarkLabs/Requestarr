#!/usr/bin/env bash
# Local dev/test stack for Requestarr.
#
#   docker/dev.sh up                    build and start everything, wait for health checks, run check
#   docker/dev.sh check                 health and status checks for Requestarr and the test backends
#   docker/dev.sh down                  stop the stack (data kept)
#   docker/dev.sh reset-test-backends   wipe the Sonarr/Radarr test instances back to empty and restart them
#   docker/dev.sh init                  create docker/secrets.env from the template (run by `up`)
#   docker/dev.sh logs [service]        follow logs
#
# Secret values are read from docker/secrets.env and never printed.
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
    logs) shift; compose logs -f "$@" ;;
    *)
        sed -n '2,11p' "$0"
        exit 1
        ;;
esac
