#!/bin/sh
# Runs inside the test-backends-init container. Idempotent: safe after a restart or reset.
set -eu

mkdir -p /data/tv /data/movies
chown 1000:1000 /data/tv /data/movies

ensure_root_folder() {
    name="$1"; url="$2"; key="$3"; path="$4"

    for attempt in $(seq 1 30); do
        if existing=$(curl -fsS -H "X-Api-Key: $key" "$url/api/v3/rootfolder"); then
            break
        fi
        echo "$name API not ready (attempt $attempt), retrying"
        sleep 2
    done

    if echo "$existing" | grep -q "\"$path\""; then
        echo "$name root folder $path already present"
    else
        curl -fsS -X POST -H "X-Api-Key: $key" -H "Content-Type: application/json" \
            -d "{\"path\":\"$path\"}" "$url/api/v3/rootfolder" > /dev/null
        echo "$name root folder $path created"
    fi
}

ensure_root_folder Sonarr http://sonarr-test:8989 "$SONARR_TEST_API_KEY" /data/tv
ensure_root_folder Radarr http://radarr-test:7878 "$RADARR_TEST_API_KEY" /data/movies

touch /tmp/ready
echo "Test backends ready"
exec tail -f /dev/null
