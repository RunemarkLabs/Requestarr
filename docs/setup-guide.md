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
   - Permissions: tick Manage Roles, Manage Channels, View Channels, Send Messages, Embed Links, Read Message History, and Pin Messages.
     Pin Messages lets Requestarr pin the panels in each channel (section 5).
   - Integration Type: **Guild Install**.

   The generated link ends with `permissions=2251800082205712&integration_type=0&scope=bot+applications.commands`.

   **Already added the bot without Pin Messages?** You don't need to re-invite it. Open **Server Settings → Roles**, pick the bot's role, turn on **Pin Messages**, and save. Then restart Requestarr, and it pins the panels on start.
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

# Section 5 explains the Discord settings.
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

Requestarr sends movie requests to Radarr and TV requests to Sonarr. Supported versions are **Radarr 5 or later** and **Sonarr 4 or later**. Older versions are refused, with a clear message under **System → Status**.

### 4.1 API keys

In each Sonarr and Radarr, open **Settings → General → Security** and copy the **API Key**. Put it in `secrets.env`, never in `provisioning.yml`:

```ini
RADARR_API_KEY=paste-radarr-key
SONARR_API_KEY=paste-sonarr-key
```

### 4.2 Provisioning entries

Add a `backends` section to `provisioning.yml`. Each type takes a list:

```yaml
backends:
  radarr:
    - name: Radarr                  # unique name, used by /admin user access
      url: http://radarr:7878       # as Requestarr reaches it (container name on the same Docker network)
      apiKey: ${RADARR_API_KEY}
      qualityProfile: HD-1080p      # profile name (or ID) in that Radarr
      rootFolder: /movies           # must already exist as a root folder in that Radarr
      default: true                 # used for users without an assigned instance
  sonarr:
    - name: Sonarr
      url: http://sonarr:8989
      apiKey: ${SONARR_API_KEY}
      qualityProfile: HD-1080p
      rootFolder: /tv
      seriesType: standard          # standard | anime | daily
      default: true
```

| Setting | Required | Notes |
|---|---|---|
| `name` | yes | Unique across all instances. |
| `url` | yes | Base URL, without `/api`. |
| `apiKey` | yes | Use a `${VAR}` reference to `secrets.env`. |
| `qualityProfile` | no | Defaults to the instance's first profile. |
| `rootFolder` | no | Defaults to the instance's first root folder. |
| `seriesType` | no | Sonarr only. Applied to every series this instance gets from Requestarr. |
| `default` | no | One per type. Without it, the first listed instance is the default. |
| `enabled` | no | `false` keeps the instance but stops using it. |

On every start, Requestarr checks each instance:

- it can connect
- the app and version are supported
- the quality profile exists
- the root folder exists

Problems show as a health error under **System → Status**, naming the instance and the cause.

The file is the source of truth. An instance removed from the file is disabled, not deleted, so existing requests keep their history.

### 4.3 Several instances of one type

List as many instances per type as you need. Common setups:

- a separate Radarr for 4K
- a separate Sonarr for anime with `seriesType: anime`

```yaml
  radarr:
    - name: Radarr
      url: http://radarr:7878
      apiKey: ${RADARR_API_KEY}
      default: true
    - name: Radarr 4K
      url: http://radarr4k:7878
      apiKey: ${RADARR_4K_API_KEY}
      qualityProfile: Ultra-HD
      rootFolder: /movies-4k
```

### 4.4 Which instance a user's requests go to

Users never pick an instance. By default every user's movie requests go to the default Radarr, and TV requests to the default Sonarr.

To send one user's requests elsewhere, add `instance` to `/admin user access`:

```
/admin user access member:@someone type:Movies level:Auto-approve instance:Radarr 4K
```

The assignment is per user and per content type. Run `/admin user info` to see it.

### 4.5 What happens to a request

1. The user types `/request` in their channel and picks a type. The type list only shows types they have access to. Then they enter a title, or an ID such as `tmdb:603` (movies) or `tvdb:12345` (TV). Results come from their instance's own search, so no extra API keys are needed.
2. They pick a title and see a card with the poster and overview. The card says if the title is:
   - already in the library
   - already requested by someone, in which case they can follow that request instead of making a second one
   - previously denied, and why
3. For TV, they pick specific seasons, all seasons, and optionally **Future seasons**, which keeps monitoring new seasons as they air.
4. What happens next depends on their access level:
   - **off:** refused
   - **needs approval:** an Approve / Deny card appears in `#requestarr-admin`; Deny asks for a reason
   - **auto-approve:** sent straight away
5. When a request is approved, Requestarr adds it to the instance with the instance's quality profile, root folder and series type, monitored, and starts a search. The user is told the outcome in their channel.

Quotas (section 5.6) count requests a user makes over a rolling window. Following someone else's request doesn't count. When a user hits the limit, Requestarr tells them when their next request frees up.

## 5. Admin setup in Discord

### 5.1 Connect the bot

Add the Discord section to `provisioning.yml`, and put the values in `secrets.env`:

```yaml
discord:
  botToken: ${DISCORD_BOT_TOKEN}
  applicationId: ${DISCORD_APPLICATION_ID}   # Developer Portal → General Information
  guildId: ${DISCORD_GUILD_ID}               # your server's ID
  # adminRoleName: Requestarr Admin          # optional, see 5.2
  # adminRoleId: 123456789012345678          # optional: use an existing role instead
```

To copy IDs, turn on Developer Mode in Discord (User Settings → Advanced). Then right-click the server or role and choose **Copy ID**.

Restart Requestarr. The bot comes online, and **System → Status** in the web portal shows a Discord health warning or error until it connects. If the bot stays disconnected for 10 minutes, Requestarr exits so that Docker restarts it with a fresh connection.

### 5.2 Who is an admin

- **The server owner** is always a Requestarr admin and never needs a role.
- **Anyone holding the "Requestarr Admin" role** is an admin too. Requestarr creates this role on first start if it doesn't exist. It is placed below the bot's own role and has no Discord permissions of its own.

To make someone a co-admin, give them the role in **Server Settings → Roles** or from their member profile. To remove admin, take the role away.

Anyone who can manage roles in your server can hand out this role. Discord doesn't let Requestarr see who granted it, so keep Manage Roles to people you trust.

Two optional settings in `provisioning.yml` change which role counts:

- **`adminRoleName`:** a different name. An existing role with that name is reused.
- **`adminRoleId`:** a specific role you already have, used instead of creating one.

Requestarr checks admin status itself on every command and button click. What Discord shows or hides changes nothing about who may act.

### 5.3 Who sees which commands

Requestarr registers exactly two commands:

- **`/request`** is visible to everyone. Its type list is filled in for each person from their own access, so someone without TV access never sees TV.
- **`/admin`** holds every admin command: `/admin user …`, `/admin queue`, `/admin approve`, `/admin deny` and `/admin mediaserver …`. It is registered so that only members with **Manage Server** see it, which means ordinary users never do.

Everything else users need is a button: on their pinned panel (5.5) or on their request cards.

**Optional:** co-admins who hold the Requestarr Admin role but not Manage Server won't see `/admin`. You can also keep `/admin` out of every channel except the admin channel. As the server owner:

1. Open **Server Settings → Integrations → (your Requestarr bot) → Manage**.
2. Under **Commands**, pick `/admin`.
3. Under **Roles & Members**, add the **Requestarr Admin** role and allow it.
4. Under **Channels**, turn off **All Channels** and allow only `#requestarr-admin`.

This is display only. Requestarr still refuses `/admin` from non-admins and outside the admin channel.

### 5.4 The admin channel and request channels

On first start, Requestarr creates two things, or reuses them if they already exist:

- **`#requestarr-admin`:** run admin commands here. Approval cards for requests and media server access also appear here. Only the Requestarr Admin role, the owner, and the bot can see it. Admins can also make their own requests here; those go straight through.
- **A "Requestarr Requests" category:** each user's private request channel goes here. When a category reaches Discord's limit of 50 channels, Requestarr starts "Requestarr Requests 2", and so on.

You can rename or move the channel and category. Requestarr tracks them by ID.

The admin channel has a pinned panel with two buttons:

- **Pending queue:** lists requests waiting for approval.
- **Users:** lists everyone with their status, channel and access.

Requestarr posts the panel on first start and re-posts it if it is deleted.

Admin commands work only in the admin channel. Users' commands and panel buttons work only in their own channel.

### 5.5 Add your first user

In `#requestarr-admin`, run:

```
/admin user add member:@someone
```

Requestarr then:

1. Creates the Requestarr user from the member's Discord account.
2. Creates a role just for them (`requestarr-{name}`) and gives it to them.
3. Creates their private channel (`#requests-{name}`). Only that role, the admin role (and the owner), and the bot can see it.
4. Posts a welcome message there, then pins the user's **panel**. The panel says what they may request and has three buttons:
   - **My requests:** their requests and the ones they follow.
   - **My limits:** their quota usage.
   - **Link Plex:** a short form for their Plex username, which sends you an access request. This button disappears once an account is linked.

   The panel updates itself when you change their access or link their account.

By default, a new user gets:

- **Movies and TV:** needs approval.
- **Music:** off.

To set other levels at add time, use the optional `movie`, `tv`, and `music` options.

Discord allows 250 roles per server, and every user needs one. `/admin user add` stops a few roles short of that limit and says so.

### 5.6 Access levels and quotas

There are three access levels for each content type:

| Level | Meaning |
|---|---|
| off | The user can't request this type. |
| needs approval | Requests wait for an admin to approve them. |
| auto-approve | Requests go straight through. |

Change a level at any time:

```
/admin user access member:@someone type:Movies level:Auto-approve
```

Quotas limit how many requests a user can make in a rolling window. To allow 5 movie requests every 7 days:

```
/admin user quota member:@someone type:Movies limit:5 days:7
```

`limit:0` removes the quota.

Comics and books are not offered yet.

### 5.7 Other user commands

| Command | What it does |
|---|---|
| `/admin user info member:@someone` | Shows status, channel, access levels, quotas, and any linked media server account. |
| `/admin user suspend member:@someone` | Locks their channel: they can read it but can't post or request. |
| `/admin user unsuspend member:@someone` | Unlocks it. |
| `/admin user remove member:@someone` | Archives their channel (renamed `archived-…`, visible only to admins), deletes their role, and keeps their request history. Run `/admin user add` to bring them back later. |
| `/admin user mediaserver member:@someone service:Plex username:{plex username}` | Links their Plex account. |
| `/admin user access … instance:{name}` | Sends that user's requests of that type to a specific Sonarr or Radarr (section 4.4). |

For every command, see the [command reference](commands.md).

Users can also ask for media server access themselves with the **Link Plex** button on their panel. An Approve / Deny card then appears in the admin channel; `/admin mediaserver approve` and `deny` do the same. Approving links the account and removes the Link Plex button from their panel. Plex invites are sent automatically in a later release.

### 5.8 When someone leaves the server

If a user leaves or is removed from the Discord server, Requestarr suspends them automatically and locks their channel. Nothing is deleted. If they rejoin, run `/admin user unsuspend`: Requestarr gives their role back and unlocks the channel.

Every action in this section, whether an admin or Requestarr did it, is recorded in Requestarr's audit log.

## 6. Connect Plex (optional)

_Coming in Phase 3:_ Plex token and library-to-content-type mapping.

## 7. Lidarr (optional)

_Coming in Phase 2._

## 8. Troubleshooting

- **"Private application cannot have a default authorization link":** set Installation → Install Link to None first.
- **The bot cannot create or assign roles:** move the bot's role higher in Server Settings → Roles.
- **Members leaving are not detected:** Server Members Intent is off.
- **"Only the Requestarr admin can use this command":** you are not the server owner and don't have the role set in `adminRoleId`.
- **"Admin commands only work in the Requestarr admin channel":** run `/admin …` commands in `#requestarr-admin`, not in a user's channel.
- **"Backend check failed" under System → Status:** the message names the instance and the cause: wrong URL or API key, an unsupported version (Radarr 5+ and Sonarr 4+ only), or a quality profile or root folder that doesn't exist in that instance. Fix `provisioning.yml` and restart.
- **Users don't see /admin:** that's intended; it only shows to members with Manage Server (see 5.3).
- **The panels are posted but not pinned, and System → Status warns about Pin Messages:** the bot's role lacks **Pin Messages**. Everything else keeps working. Turn it on in Server Settings → Roles (see section 2, step 5) and restart Requestarr.
- **The /request type list is empty:** that user has every type set to off. Change it with `/admin user access`.
- **Discord still shows old commands after an update:** press Ctrl+R in the Discord app, or fully quit it from the system tray and reopen it.
- **The bot doesn't come online:** check **System → Status** for the Discord message. Common causes are a wrong token, the bot not being invited to the server in `guildId`, or a missing application ID.
- **"Provisioning file could not be applied" under System → Status:** the message names the problem, for example an environment variable that is not set or an invalid value. Fix `provisioning.yml` or `secrets.env`, then `docker compose restart requestarr`.
- **The container restarts or stays unhealthy:** check `docker compose logs requestarr`. A common cause is a `./config` folder the container cannot write to (see section 3.2).
