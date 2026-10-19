# Requestarr setup guide

> Requestarr is in early development. This guide grows as each phase ships. Sections marked "coming" are not usable yet.

## 1. Before you start

You need:

- A Discord server where you are the owner or have Manage Server.
- Docker and Docker Compose on the host that will run Requestarr.
- Sonarr 4 and Radarr 5 or later, reachable from that host. Lidarr is optional.
- Optional: a Plex server, for library sharing.

## 2. Create your Discord bot

Every Requestarr install uses its own bot. Discord's official reference is https://discord.com/developers/docs/quick-start/getting-started.

1. Open https://discord.com/developers/applications and choose **New Application**. Name it, for example "Requestarr".
2. **General Information:** an icon and description are optional. Leave Interactions Endpoint URL, Linked Roles, Terms of Service, and Privacy Policy empty. They are only for public verified bots, which a self-hosted bot never needs.
3. **Installation:** set Installation Contexts to **Guild Install** only, set **Install Link** to **None**, and save.
   Do this before step 4. Discord refuses to make a bot private while an install link is set ("Private application cannot have a default authorization link").
4. **Bot:**
   - Set the bot username.
   - Turn **Public Bot** off, so nobody else can add your bot.
   - Under Privileged Gateway Intents, turn **Server Members Intent** on. Requestarr needs it to detect members leaving. Leave Presence and Message Content off.
   - Ignore the Bot Permissions grid on this page. It is only a calculator and saves nothing.
   - Save.
5. **OAuth2 → URL Generator:**
   - Scopes: tick `bot` and `applications.commands`.
   - Permissions: tick Manage Roles, Manage Channels, View Channels, Send Messages, Embed Links, and Read Message History.
   - Integration Type: **Guild Install**.

   The generated link ends with `permissions=268520464&integration_type=0&scope=bot+applications.commands`.
6. Open the generated link, pick your server, and authorize. The bot shows as offline until Requestarr runs.
7. **Bot → Reset Token**, then paste the token straight into Requestarr's secrets file (section 3). Never paste it into chat, issues, or screenshots. If you lose it, reset it again.
8. In your server, open **Server Settings → Roles** and keep the bot's role above any roles it should manage.

Do not verify a self-hosted bot. Verification is only for bots in 100 or more servers, and it permanently locks the name and owner.

## 3. Install Requestarr (Docker Compose)

There is no published image yet, so you build it from source. This needs Git and Docker with Compose v2.

### 3.1 Build the image

```bash
git clone https://github.com/RunemarkLabs/Requestarr.git
cd Requestarr
docker build -f docker/Dockerfile -t requestarr:latest .
```

The image runs Requestarr on .NET 10 as a non-root user. It keeps all its data in `/config` and listens on port 9797.

### 3.2 Create an install folder

Put three files in a folder of your choice, for example `requestarr/`.

**`compose.yaml`**

```yaml
services:
  requestarr:
    image: requestarr:latest
    container_name: requestarr
    env_file: secrets.env
    environment:
      REQUESTARR_PROVISIONING_FILE: /provisioning/provisioning.yml
      TZ: Etc/UTC
    volumes:
      - ./config:/config
      - ./provisioning.yml:/provisioning/provisioning.yml:ro
    ports:
      - "9797:9797"
    restart: unless-stopped
```

The image runs as user ID 1654. If you bind-mount `./config` as above, make that folder writable for it (`mkdir config && sudo chown 1654:1654 config`), or use a named volume instead.

**`secrets.env`**

This holds every secret. Keep it out of version control and backups that others can read. On Linux, `chmod 600 secrets.env`.

```ini
REQUESTARR_ADMIN_USERNAME=admin
REQUESTARR_ADMIN_PASSWORD=choose-a-long-password
REQUESTARR_API_KEY=at-least-20-random-characters
# Section 2, step 7
DISCORD_BOT_TOKEN=
```

To generate random values, run `openssl rand -hex 16` once per value.

**`provisioning.yml`**

Requestarr reads this on every start, so the configuration lives in a file instead of clicks.

- `${NAME}` is replaced with the value from `secrets.env`.
- `${NAME:-default}` supplies a default when the variable is empty.
- Keys in the file win over changes made in the web portal; keys you leave out are not touched.

```yaml
version: 1

general:
  instanceName: Requestarr
  logLevel: info

auth:
  method: forms                       # web portal login page
  required: enabled                   # or disabledForLocalAddresses
  username: ${REQUESTARR_ADMIN_USERNAME}
  password: ${REQUESTARR_ADMIN_PASSWORD}
  apiKey: ${REQUESTARR_API_KEY}

# Recognised now, used once Discord support ships (Phase 1).
discord:
  botToken: ${DISCORD_BOT_TOKEN:-}
```

The `backends` section (Sonarr, Radarr, and Lidarr, with any number of instances each) and the `mediaServers` section are described in section 4 and section 6 once they ship. Their format is already checked, so a typo shows up at startup.

### 3.3 First start

```bash
docker compose up -d
docker compose logs requestarr | grep -i provisioning
```

A successful start logs `Provisioning applied from /provisioning/provisioning.yml`. If the file has an error, the log names the line or the missing variable, and the web portal shows it under **System → Status**.

### 3.4 Check that it is healthy

```bash
docker compose ps                      # STATUS shows (healthy) after about 30 seconds
curl http://localhost:9797/ping        # {"status": "OK"}
```

The image has a built-in health check that calls `/ping`.

### 3.5 Open the web portal

Browse to `http://{host}:9797` and sign in with the username and password from `secrets.env`. The portal is for administrators only. Users never log in to it; they use Discord.

What exists today:

- **Settings:** General, UI, and Tags.
- **System:** Status, Tasks, Backup, Updates, Events, and Log Files.

Request management arrives with Phase 1.

### 3.6 Updating

Pull the source, rebuild the image, and restart:

```bash
git pull
docker build -f docker/Dockerfile -t requestarr:latest .
docker compose up -d
```

Requestarr never updates itself inside the container.

## 4. Connect Sonarr and Radarr

_Coming in Phase 1:_ API keys, multiple instances, automatic webhook registration, the polling-only option, and series type per Sonarr instance.

## 5. Admin setup in Discord

_Coming in Phase 1:_ admin role and admin channel, your first `/user add`, access levels, and quotas.

## 6. Connect Plex (optional)

_Coming in Phase 3:_ Plex token and library-to-content-type mapping.

## 7. Lidarr (optional)

_Coming in Phase 2._

## 8. Troubleshooting

- **"Private application cannot have a default authorization link":** set Installation → Install Link to None first.
- **The bot cannot create or assign roles:** move the bot's role higher in Server Settings → Roles.
- **Members leaving are not detected:** Server Members Intent is off.
- **"Provisioning file could not be applied" under System → Status:** the message names the problem, for example an environment variable that is not set or an invalid value. Fix `provisioning.yml` or `secrets.env`, then `docker compose restart requestarr`.
- **The container restarts or stays unhealthy:** check `docker compose logs requestarr`. A common cause is a `./config` folder the container cannot write to (see section 3.2).
