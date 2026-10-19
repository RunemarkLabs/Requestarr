"""Remove everything Requestarr created in the Discord test server, for a clean UAT start.

Deletes: #requestarr-admin, the "Requestarr Requests" categories and every channel in them
(including archived ones), and the per-user "requestarr-*" roles.
Keeps: the bot's own role, the Requestarr Admin role, and anything else in the server.

Reads DISCORD_BOT_TOKEN and DISCORD_TEST_GUILD_ID from the environment. Never prints the token.
Run through `docker/dev.sh reset-uat`.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://discord.com/api/v10"
ADMIN_CHANNEL = "requestarr-admin"
CATEGORY = "Requestarr Requests"
USER_ROLE_PREFIX = "requestarr-"


def call(method, path):
    request = urllib.request.Request(API + path, method=method, headers={
        "Authorization": "Bot " + os.environ["DISCORD_BOT_TOKEN"],
        "User-Agent": "DiscordBot (https://github.com/RunemarkLabs/Requestarr, 1.0)",
    })
    while True:
        try:
            with urllib.request.urlopen(request) as response:
                body = response.read()
                return json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            if error.code == 429:
                time.sleep(float(json.loads(error.read()).get("retry_after", 1)) + 0.1)
                continue
            raise SystemExit(f"Discord refused {method} {path}: HTTP {error.code}")


def main():
    guild = os.environ.get("DISCORD_TEST_GUILD_ID")
    if not os.environ.get("DISCORD_BOT_TOKEN") or not guild:
        raise SystemExit("DISCORD_BOT_TOKEN and DISCORD_TEST_GUILD_ID must be set in docker/secrets.env")

    channels = call("GET", f"/guilds/{guild}/channels")
    categories = {c["id"] for c in channels if c["type"] == 4 and c["name"].startswith(CATEGORY)}
    doomed = [c for c in channels if c["parent_id"] in categories or (c["type"] == 0 and c["name"] == ADMIN_CHANNEL)]
    doomed += [c for c in channels if c["id"] in categories]

    for channel in doomed:
        call("DELETE", f"/channels/{channel['id']}")
        print(f"deleted channel {'category ' if channel['type'] == 4 else '#'}{channel['name']}")

    for role in call("GET", f"/guilds/{guild}/roles"):
        if role["name"].startswith(USER_ROLE_PREFIX) and not role.get("managed"):
            call("DELETE", f"/guilds/{guild}/roles/{role['id']}")
            print(f"deleted role {role['name']}")

    left = call("GET", f"/guilds/{guild}/channels")
    remaining = [c["name"] for c in left if c["parent_id"] in categories or c["id"] in categories or c["name"] == ADMIN_CHANNEL]
    print(f"Requestarr channels left in the server: {len(remaining)}")
    sys.exit(1 if remaining else 0)


if __name__ == "__main__":
    main()
