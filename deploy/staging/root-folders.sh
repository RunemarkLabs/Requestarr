#!/bin/sh
# Runs once in the root-folders container: makes /tv-beta and /movies-beta root folders in the beta Sonarr and Radarr.
# Idempotent: safe on every start. Never adds any other folder.
set -eu

ensure_root_folder() {
    name="$1"; url="$2"; key="$3"; path="$4"

    for attempt in $(seq 1 30); do
        if existing=$(curl -fsS -H "X-Api-Key: $key" "$url/api/v3/rootfolder"); then
            break
        fi
        echo "$name isn't answering yet (attempt $attempt), retrying"
        sleep 2
    done

    if echo "$existing" | grep -q "\"$path\""; then
        echo "$name root folder $path already there"
    else
        curl -fsS -X POST -H "X-Api-Key: $key" -H "Content-Type: application/json" \
            -d "{\"path\":\"$path\"}" "$url/api/v3/rootfolder" > /dev/null
        echo "$name root folder $path added"
    fi
}

ensure_root_folder "Sonarr (beta)" http://sonarr:8989 "$SONARR_API_KEY" /tv-beta
ensure_root_folder "Radarr (beta)" http://radarr:7878 "$RADARR_API_KEY" /movies-beta
