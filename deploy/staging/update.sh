#!/usr/bin/env bash
# Updates the beta Requestarr, or rolls it back. Run it in this folder on the server.
#
#   ./update.sh            pull the newest staging image; if it's newer than what runs, back up Requestarr's settings
#                          and data, then restart on it (Sonarr and Radarr are left alone)
#   ./update.sh rollback   go back to the version that ran before the last update, with the data backed up then
#                          (anything done in Discord since that update is lost), and stay on it until ./update.sh
#   ./update.sh status     which version runs now, and what a rollback would go back to
#
# Backups go to ./backups (one per update; delete old ones when you like).
set -euo pipefail
cd "$(dirname "$0")"

VOLUME=requestarr-beta_requestarr-config
IMAGE=ghcr.io/runemarklabs/requestarr

die() {
    echo "update.sh: $*" >&2
    exit 1
}

[ -f .env ] || die "no .env here: copy env.example to .env and fill it in first (docs/setup-guide.md)"
docker compose version >/dev/null 2>&1 || die "needs Docker with the compose plugin (docker compose)"

# The version label of the image the running Requestarr container uses (empty when it isn't running).
running_version() {
    local container
    container=$(docker compose ps -q requestarr 2>/dev/null || true)
    [ -n "$container" ] || return 0
    docker inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$container" 2>/dev/null || true
}

running_image() {
    local container
    container=$(docker compose ps -q requestarr 2>/dev/null || true)
    [ -n "$container" ] && docker inspect --format '{{.Image}}' "$container" 2>/dev/null || true
}

image_version() {
    docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$1" 2>/dev/null || true
}

# Sets or clears REQUESTARR_TAG in .env (the only line this script ever changes).
pin() {
    local tag="$1" rest
    rest=$(grep -v '^REQUESTARR_TAG=' .env || true)
    if [ -n "$tag" ]; then
        printf '%s\nREQUESTARR_TAG=%s\n' "$rest" "$tag" > .env.new
    else
        printf '%s\n' "$rest" > .env.new
    fi
    chmod --reference=.env .env.new 2>/dev/null || chmod 600 .env.new
    mv .env.new .env
}

backup() {
    local version="$1" file
    mkdir -p backups
    file="backups/requestarr-config-${version:-unknown}-$(date +%Y%m%d-%H%M%S).tar.gz"
    docker run --rm -v "$VOLUME:/config:ro" -v "$PWD/backups:/backups" busybox:stable \
        tar czf "/backups/$(basename "$file")" -C /config .
    echo "$file"
}

wait_healthy() {
    docker compose up -d --wait requestarr >/dev/null || die "Requestarr didn't come up healthy: docker compose logs requestarr"
}

update() {
    local before before_image after
    before=$(running_version)
    before_image=$(running_image)

    pin ""
    echo "Pulling the newest staging image…"
    docker compose pull -q requestarr
    after=$(image_version "$IMAGE:staging")

    if [ -n "$before_image" ] && [ "$before_image" = "$(docker image inspect --format '{{.Id}}' "$IMAGE:staging")" ]; then
        echo "Already on the newest staging version ($after). Nothing to do."
        return 0
    fi

    if [ -n "$before" ]; then
        echo "Stopping Requestarr $before and backing up its data…"
        docker compose stop requestarr >/dev/null
        local file
        file=$(backup "$before")
        printf '%s %s\n' "$before" "$file" > .previous-version
        echo "Backed up to $file"
    fi

    wait_healthy
    echo "Requestarr updated: ${before:-not running} -> $(running_version). Rollback: ./update.sh rollback"
}

rollback() {
    [ -f .previous-version ] || die "no update to roll back (./update.sh records the version it replaced)"
    local version file
    read -r version file < .previous-version
    [ -f "$file" ] || die "the backup $file is gone; to go back without it: set REQUESTARR_TAG=$version in .env, then docker compose up -d"

    echo "Rolling back from $(running_version) to $version, with the data from $file…"
    docker pull -q "$IMAGE:$version" >/dev/null
    docker compose stop requestarr >/dev/null
    docker run --rm -v "$VOLUME:/config" -v "$PWD/backups:/backups:ro" busybox:stable \
        sh -c "find /config -mindepth 1 -delete && tar xzf /backups/$(basename "$file") -C /config"
    pin "$version"
    wait_healthy
    rm -f .previous-version
    echo "Requestarr rolled back to $(running_version) and stays on it (REQUESTARR_TAG in .env). ./update.sh goes back to the newest staging version."
}

status() {
    echo "Running: Requestarr $(running_version) ($(grep -E '^REQUESTARR_TAG=' .env || echo 'newest staging image'))"
    if [ -f .previous-version ]; then
        echo "Rollback would go back to: $(cut -d' ' -f1 .previous-version)"
    else
        echo "Rollback: nothing recorded"
    fi
}

case "${1:-update}" in
    update) update ;;
    rollback) rollback ;;
    status) status ;;
    *)
        sed -n '2,11p' "$0"
        exit 1
        ;;
esac
