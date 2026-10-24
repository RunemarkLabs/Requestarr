"""Make a title in a local stack's TEST Radarr or Sonarr look downloaded, the way a real download is imported.

    fake-download.py movie "<title>"
    fake-download.py tv "<title>" <season>

A placeholder video file (sparse: it takes no disk space) is created in a staging folder inside the test
container, as the container's app user (1000), and handed to Radarr or Sonarr through their own Manual
Import. They move it into the title's folder and send their normal import webhook, so Requestarr sees it
exactly as it sees a real download (and its backup poll would find it too). TV: every aired episode of
the season that has no file yet.

Only ever touches one local stack's test containers (compose project requestarr-qa or requestarr-dev, from
REQUESTARR_COMPOSE_PROJECT; services radarr-test and sonarr-test) and their localhost ports. Reads the API
keys and ports from the environment (dev.ps1 / dev.sh pass them from docker/secrets.env for the chosen
stack) and never prints them. Needs Python 3 and Docker.
"""
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

STACKS = ("requestarr-qa", "requestarr-dev")
PROJECT = os.environ.get("REQUESTARR_COMPOSE_PROJECT", "requestarr-qa")
APPS = {
    "movie": {"service": "radarr-test", "app": "Radarr", "port": "RADARR_TEST_PORT", "default_port": "17878", "key": "RADARR_TEST_API_KEY"},
    "tv": {"service": "sonarr-test", "app": "Sonarr", "port": "SONARR_TEST_PORT", "default_port": "18989", "key": "SONARR_TEST_API_KEY"},
}


def fail(message):
    print(f"fake-download: {message}", file=sys.stderr)
    sys.exit(1)


def container(service):
    """The running test container for the service, found by its compose labels; refuses anything else."""
    if PROJECT not in STACKS:
        fail(f"{PROJECT} isn't a local test stack ({' or '.join(STACKS)}).")
    out = subprocess.run(["docker", "ps", "--filter", f"label=com.docker.compose.project={PROJECT}",
                          "--filter", f"label=com.docker.compose.service={service}", "--format", "{{.Names}}"],
                         capture_output=True, text=True)
    if out.returncode != 0:
        fail("Docker isn't answering. Start Docker Desktop, then the stack (dev.ps1 up).")
    names = out.stdout.split()
    if len(names) != 1:
        fail(f"the {PROJECT.split('-')[1]} stack's {service} container isn't running (dev.ps1 up, or dev.ps1 start-backend).")
    return names[0]


def docker_exec(name, user, *command):
    out = subprocess.run(["docker", "exec", "-u", user, name, *command], capture_output=True, text=True)
    if out.returncode != 0:
        fail(f"inside {name}: {out.stderr.strip() or out.stdout.strip()}")
    return out.stdout


class Api:
    def __init__(self, app):
        self.base = f"http://localhost:{os.environ.get(app['port']) or app['default_port']}/api/v3/"
        self.key = os.environ.get(app["key"])
        self.name = app["app"]
        if not self.key:
            fail(f"{app['key']} isn't set (run this through dev.ps1 or dev.sh, which read docker/secrets.env).")

    def call(self, method, resource, body=None, **query):
        url = self.base + resource + ("?" + urllib.parse.urlencode(query) if query else "")
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(url, data=data, method=method, headers={"X-Api-Key": self.key, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                text = response.read().decode()
                return json.loads(text) if text else None
        except urllib.error.HTTPError as error:
            fail(f"the test {self.name} refused {method} {resource}: HTTP {error.code} {error.read().decode()[:300]}")
        except urllib.error.URLError as error:
            fail(f"the test {self.name} isn't reachable at {self.base}: {error.reason}")

    def wait_command(self, command):
        for _ in range(90):
            state = self.call("GET", f"command/{command['id']}")
            if state.get("status") in ("completed", "failed", "aborted", "cancelled"):
                return state
            time.sleep(1)
        fail(f"{self.name} didn't finish the import in time")


def find(items, title, kind):
    wanted = title.strip().lower()
    label = lambda i: f"{i['title']} ({i.get('year')})"
    exact = [i for i in items if i["title"].lower() == wanted or label(i).lower() == wanted]
    found = exact or [i for i in items if wanted in i["title"].lower()]
    if len(found) == 1:
        return found[0]
    if not found:
        fail(f"no {kind} matching \"{title}\" is in the test library. Request it in Discord first, then run this again.")
    fail(f"\"{title}\" matches several: {', '.join(label(i) for i in found)}. Use the full title.")


def safe(text):
    return re.sub(r"[^A-Za-z0-9]+", ".", text).strip(".")


def staging(name, folder, title_folder):
    # The staging folder lives in the test media volume, owned by the app user like a download folder.
    # The title's own folder must exist too (Manual Import looks in it), as it would after any import.
    docker_exec(name, "0", "sh", "-c", "mkdir -p /data/downloads && chown 1000:1000 /data/downloads")
    docker_exec(name, "1000:1000", "sh", "-c", f"rm -rf '{folder}' && mkdir -p '{folder}' '{title_folder}'")


def manual_import(api, files):
    command = api.call("POST", "command", {"name": "ManualImport", "importMode": "move", "files": files})
    state = api.wait_command(command)
    if state.get("status") != "completed":
        fail(f"{api.name}'s Manual Import ended as {state.get('status')}: {state.get('message') or state.get('exception') or ''}")


def movie(title):
    app = APPS["movie"]
    name = container(app["service"])
    api = Api(app)
    item = find(api.call("GET", "movie"), title, "movie")
    label = f"{item['title']} ({item.get('year')})"
    if item.get("hasFile"):
        print(f"{label} already has a file in the test Radarr; nothing to do.")
        return

    folder = f"/data/downloads/fake-movie-{item['id']}"
    file = f"{folder}/{safe(item['title'])}.{item.get('year')}.1080p.WEB-DL.x264-FAKE.mkv"
    staging(name, folder, item["path"])
    docker_exec(name, "1000:1000", "truncate", "-s", "1500M", file)

    # Only the folder: given a movie, Radarr would look in the movie's own folder instead.
    candidates = api.call("GET", "manualimport", folder=folder, filterExistingFiles="false")
    if not candidates:
        fail("Radarr didn't see the placeholder file")
    candidate = candidates[0]
    manual_import(api, [{"path": candidate["path"], "folderName": candidate.get("folderName"), "movieId": item["id"],
                         "quality": candidate["quality"], "languages": candidate.get("languages") or [], "releaseGroup": "FAKE", "indexerFlags": 0}])

    for _ in range(30):
        if api.call("GET", f"movie/{item['id']}").get("hasFile"):
            break
        time.sleep(1)
    else:
        fail("Radarr took the import but the movie still has no file")

    print(f"Imported a placeholder file for {label} into the test Radarr ({item['path']}), the way a real download is imported.")
    print("Radarr sends its import webhook to Requestarr now; Requestarr's backup poll would also find it.")
    print("In Discord: the request message is reposted as Available (ready to watch), mentioning the requester and anyone following,")
    print("and its feed card in #requestarr-feed moves to the bottom showing Available, with Delete.")


def tv(title, season):
    app = APPS["tv"]
    name = container(app["service"])
    api = Api(app)
    item = find(api.call("GET", "series"), title, "show")
    label = f"{item['title']} ({item.get('year')})"
    seasons = [s["seasonNumber"] for s in item.get("seasons", [])]
    if season not in seasons:
        fail(f"{label} has no season {season} (it has {', '.join(map(str, seasons))})")

    now = datetime.now(timezone.utc)
    episodes = [e for e in api.call("GET", "episode", seriesId=item["id"])
                if e["seasonNumber"] == season and e.get("airDateUtc") and datetime.fromisoformat(e["airDateUtc"].replace("Z", "+00:00")) <= now]
    missing = [e for e in episodes if not e.get("hasFile")]
    if not episodes:
        fail(f"no episode of {label} season {season} has aired yet")
    if not missing:
        print(f"Every aired episode of {label} season {season} already has a file; nothing to do.")
        return

    folder = f"/data/downloads/fake-tv-{item['id']}-s{season:02d}"
    staging(name, folder, item["path"])
    names = [f"{folder}/{safe(item['title'])}.S{season:02d}E{e['episodeNumber']:02d}.1080p.WEB-DL.x264-FAKE.mkv" for e in missing]
    docker_exec(name, "1000:1000", "sh", "-c", " && ".join(f"truncate -s 900M '{n}'" for n in names))

    candidates = api.call("GET", "manualimport", folder=folder, filterExistingFiles="false")
    by_number = {e["episodeNumber"]: e["id"] for e in missing}
    files = []
    for candidate in candidates:
        number = int(re.search(r"E(\d+)", candidate["path"]).group(1))
        files.append({"path": candidate["path"], "folderName": candidate.get("folderName"), "seriesId": item["id"], "seasonNumber": season,
                      "episodeIds": [by_number[number]], "quality": candidate["quality"], "languages": candidate.get("languages") or [],
                      "releaseGroup": "FAKE", "indexerFlags": 0})
    if not files:
        fail("Sonarr didn't see the placeholder files")
    manual_import(api, files)

    for _ in range(30):
        have = sum(1 for e in api.call("GET", "episode", seriesId=item["id"]) if e["seasonNumber"] == season and e.get("hasFile"))
        if have >= len(episodes):
            break
        time.sleep(1)
    else:
        fail("Sonarr took the import but not every episode has a file")

    print(f"Imported placeholder files for {len(missing)} aired episode(s) of {label} season {season} into the test Sonarr, the way a real download is imported.")
    print("Sonarr sends its import webhook to Requestarr now; Requestarr's backup poll would also find it.")
    print("In Discord: if the request asked for more seasons, its message is reposted as Partly available (this season); once every")
    print("requested season is there, as Available. The feed card in #requestarr-feed follows, and shows Delete.")


def main(args):
    if len(args) == 2 and args[0] == "movie":
        movie(args[1])
    elif len(args) == 3 and args[0] == "tv" and args[2].isdigit():
        tv(args[1], int(args[2]))
    else:
        fail('usage: fake-download movie "<title>" | fake-download tv "<title>" <season>')


if __name__ == "__main__":
    main(sys.argv[1:])
