"""Read-only Discord status of one local stack, for `dev.ps1 check` / `dev.sh check`.

    discord-status.py <compose project>

Prints whether the stack's bot came online (from its Requestarr container's log), the server it's in, the
slash commands registered there, Requestarr's channel groups, and the latest "what's new" card in #requestarr-admin
and #requestarr-announcements. Uses only that stack's own bot (STACK_DISCORD_BOT_TOKEN and STACK_DISCORD_GUILD_ID,
which the scripts set from docker/secrets.env for the chosen stack) and only reads: it never creates, edits or
deletes anything. Never prints the token. Exits 1 if the bot isn't online or has no commands registered.
"""
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = "https://discord.com/api/v10"
READY = "Discord bot ready in server"


def line(name, value):
    print(f"{name:<26} {value}")


def call(path):
    request = urllib.request.Request(API + path, headers={
        "Authorization": "Bot " + os.environ["STACK_DISCORD_BOT_TOKEN"],
        "User-Agent": "DiscordBot (https://github.com/RunemarkLabs/Requestarr, 1.0)",
    })
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        return {"error": f"HTTP {error.code}"}
    except (urllib.error.URLError, TimeoutError) as error:
        return {"error": str(error)}


def card_title(message):
    embeds = message.get("embeds") or []
    title = embeds[0].get("title", "") if embeds else ""
    return title if title.startswith("Requestarr ") else None


def latest(channels, name):
    channel = next((c for c in channels if c.get("type") == 0 and c.get("name") == name), None)
    if channel is None:
        return f"#{name} doesn't exist yet"
    messages = call(f"/channels/{channel['id']}/messages?limit=30")
    if not isinstance(messages, list):
        return f"#{name}: {messages.get('error')}"
    post = next((m for m in messages if card_title(m)), None)
    if post is None:
        return f"#{name}: none yet"
    # ASCII only: Windows PowerShell shows an emoji or a middle dot from Python as "?".
    title = card_title(post).replace("\u00b7", "-").encode("ascii", "ignore").decode().strip()
    return f"#{name} {post['timestamp'][11:19]}Z \"{title}\""


def main():
    project = sys.argv[1] if len(sys.argv) > 1 else ""
    token, guild = os.environ.get("STACK_DISCORD_BOT_TOKEN"), os.environ.get("STACK_DISCORD_GUILD_ID")
    if not token or not guild:
        line("discord", "not set up for this stack in docker/secrets.env")
        return 0

    ok = True
    names = subprocess.run(["docker", "ps", "--filter", f"label=com.docker.compose.project={project}",
                            "--filter", "label=com.docker.compose.service=requestarr", "--format", "{{.Names}}"],
                           capture_output=True, text=True).stdout.split()
    ready = []
    # Just started (dev.ps1 up): the bot connects a few seconds after Requestarr answers, so wait up to a minute.
    for _ in range(12 if names else 0):
        log = subprocess.run(["docker", "logs", names[0]], capture_output=True, text=True, encoding="utf-8", errors="replace")
        ready = [entry for entry in (log.stdout + log.stderr).splitlines() if READY in entry]
        if ready:
            break
        time.sleep(5)
    if ready:
        line("bot online", ready[-1][ready[-1].index(READY):])
    else:
        line("bot online", "no (Requestarr isn't running, or the bot hasn't connected yet)")
        ok = False

    me, server = call("/users/@me"), call(f"/guilds/{guild}")
    if "error" in me or "error" in server:
        line("discord", f"can't read the server: {me.get('error') or server.get('error')}")
        return 1

    commands = call(f"/applications/{me['id']}/guilds/{guild}/commands")
    registered = sorted("/" + c["name"] for c in commands) if isinstance(commands, list) else []
    line("bot / server", f"{me['username']} in '{server['name']}'")
    line("commands registered", ", ".join(registered) or "none")
    ok = ok and bool(registered)

    channels = call(f"/guilds/{guild}/channels")
    channels = channels if isinstance(channels, list) else []
    groups = sorted(c["name"] for c in channels if c.get("type") == 4 and c.get("name", "").startswith("Requestarr "))
    line("channel groups", ", ".join(groups) or "none yet")
    line("what's new (admins)", latest(channels, "requestarr-admin"))
    line("what's new (users)", latest(channels, "requestarr-announcements"))
    return 0 if ok else 1


if __name__ == "__main__":
    # The console may not be UTF-8 (Windows PowerShell): never fail on an emoji.
    sys.stdout.reconfigure(errors="replace")
    sys.exit(main())
