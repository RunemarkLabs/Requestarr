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
   - Set the bot username. Leave its icon and banner empty: Requestarr sets them itself (5.13).
   - Turn **Public Bot** off, so nobody else can add your bot.
   - Under Privileged Gateway Intents, turn **Server Members Intent** on. Requestarr needs it to detect members leaving. Leave Presence and Message Content off.
   - Ignore the Bot Permissions grid on this page. It is only a calculator and saves nothing.
   - Save.
5. **OAuth2 → URL Generator:**
   - Scopes: tick `bot` and `applications.commands`.
   - Permissions: tick Manage Roles, Manage Channels, View Channels, Send Messages, Embed Links, Read Message History, and Pin Messages.
     Pin Messages lets Requestarr pin an outage notice while the outage lasts (5.7).
   - Integration Type: **Guild Install**.

   The generated link ends with `permissions=2251800082205712&integration_type=0&scope=bot+applications.commands`.

   **Already added the bot without Pin Messages?** You don't need to re-invite it. Open **Server Settings → Roles**, pick the bot's role, turn on **Pin Messages**, and save. Outage notices are pinned from then on.
6. Open the generated link, pick your server, and authorize. The bot shows as offline until Requestarr runs.
7. **Bot → Reset Token**, then paste the token straight into Requestarr's secrets file (section 3). Never paste it into chat, issues, or screenshots. If you lose it, reset it again.
8. In your server, open **Server Settings → Roles** and keep the bot's role above any roles it should manage.

Do not verify a self-hosted bot. Verification is only for bots in 100 or more servers, and it permanently locks the name and owner.

## 3. Install Requestarr (Docker Compose)

Requestarr's image is published to GitHub's container registry as `ghcr.io/runemarklabs/requestarr` for x86-64 and ARM servers, but only the early access `staging` tag so far; `latest` and version tags arrive with the first release. Until then you build it from source. This needs Git and Docker with Compose v2.

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
    - name: Radarr                  # unique name, shown on member cards
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
| `pollingOnly` | no | `true`: no webhook on this instance; downloads are found by the backup poll only (section 4.8). |

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
- a separate Sonarr for anime, or separate sub and dub anime instances (see 4.7)

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

To send one user's requests elsewhere, open their card (`/admin user`, or **Users** on the admin panel) and pick the instance in its **Limits, instance, their requests…** menu, for example "Movies: Radarr 4K". The menu only appears when a type has more than one instance.

The assignment is per user and per content type, and the card shows it.

### 4.5 What happens to a request

1. The user types `/request` in their channel and picks a type. The type list only shows types they have access to. Then they enter a title, or an ID such as `tmdb:603` (movies) or `tvdb:12345` (TV). Results come from their instance's own search, so no extra API keys are needed.
   The whole request stays in **one message** that changes in place: results, details, seasons, confirmation, final status. Each request is its own message, so separate requests don't run together.
2. Each result line shows the year, the network or studio, and either the top three cast (with The Movie Database key, see 4.6) or the genre. That helps tell apart titles with the same name.
3. They pick a title and see its card: a large poster and everything Sonarr or Radarr knows about it. That covers genres, rating, age rating, runtime, network or studio, status, seasons, air or release dates, and the overview. The card also shows the top cast and episode count when The Movie Database key is set, and which instance it goes to. The card says if the title is:
   - already in the library
   - already requested by someone, in which case they can follow that request instead of making a second one
   - previously denied, and why
4. For TV, they choose one of two paths:
   - **All seasons:** sends the request straight away for every season now, and new seasons as they air (Sonarr's monitor-new-seasons setting). Specials (season 0) are not included.
   - **Choose seasons:** pick individual seasons, including **Specials** if they want them, and optionally **Also new seasons**. Every season of any show can be picked. Long shows (more than 24 seasons) page through them with ◀ / ▶, and picks are kept across pages. After **Continue**, Requestarr restates the choice in plain words, for example "seasons 1–2 and 36 only; new seasons won't be added", and they press **Request** or **Back**.

   Either way, the final status says exactly which seasons were requested.
5. What happens next depends on their access level:
   - **off:** refused
   - **needs approval:** an Approve / Deny card appears in `#requestarr-admin`; Deny asks for a reason
   - **auto-approve:** sent straight away
6. The message ends with a clear status: **Request sent**, **Waiting for approval** (with a Cancel button), **Following**, **Already available**, or **Not requested** with the reason. TV requests also get **Add seasons**: the owner, anyone following, or an admin can add more seasons (or new seasons as they air) at any time, without approval and without using a limit. Seasons are never removed there.
7. When a request is approved, Requestarr adds it to the instance with the instance's quality profile, root folder and series type, monitored, and starts a search. After that the message follows the download: downloading, stuck or failed, partly available, available (section 4.8).
8. A request for a title that's already in the library when you act ends as 🎉 **Already available** with a "good news" message to the user (never denied, cancelled or failed, whichever you press, and also when Sonarr or Radarr reports it was added meanwhile); use the **Already available** button on the approval card, feed card or request card when you have it some other way.
9. Whenever a request changes later (approved, already available, denied, cancelled by an admin, switched to another anime language, downloading, available, or seasons added by someone else), the user's old request message is deleted and a fresh one is posted at the bottom of their channel. It mentions them, so they get a ping, and only has the buttons that still make sense (Cancel while it waits for approval, none once decided). Followers' messages get the same treatment. The approval card is edited in place. Details:
   - The user's own **Cancel** edits their message in place, without a repost or ping.
   - A message that's still in the middle of its steps (results, seasons, confirmation) is never reposted.
   - If the bot can't delete the old message, the new one is still posted and the old one loses its buttons.
   - No separate notice repeats the news. A notice is sent only to someone with no message to repost: the message was deleted, or the request is from before this version.

   Buttons clicked late re-check the request and never change one that's already decided.

Quotas (section 5.6) count requests a user makes over a rolling window. Following someone else's request doesn't count. When a user hits the limit, Requestarr tells them when their next request frees up.

### 4.6 Cast from The Movie Database (optional)

Sonarr and Radarr don't return cast. With a free The Movie Database key, Requestarr adds the top three cast to each result line and the card, and the episode count to TV cards. Without a key everything still works: result lines show the genre instead, and nothing errors.

1. Create an account at themoviedb.org, then open **Settings → API** and request a developer key (personal use is free).
2. Copy the **API Read Access Token** (the long one). The shorter v3 API key also works.
3. Add it to `secrets.env`:

   ```
   TMDB_API_KEY=your-token
   ```

4. Reference it in `provisioning.yml`:

   ```yaml
   tmdb:
     apiKey: ${TMDB_API_KEY:-}
   ```

5. Restart Requestarr.

The key is kept in memory only. It is never written to the database or logs. Cast lookups are cached for 12 hours and give up after a few seconds, so a slow or unavailable The Movie Database never holds up a request.

**Attribution:** TMDB's terms ask apps to use TMDB's logo, keep it less prominent than the app's own branding, and show the notice "… uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB." in an About or Credits area. Requestarr does this while a key is set:

- **Cards with cast:** a short "Cast from TMDB" footer with TMDB's small logo.
- **Result lists with cast:** a "Cast from TMDB" line.
- **`/help` and `/admin help`:** a **Credits** topic with TMDB's logo and the full notice.

The logo is TMDB's approved short logo, unaltered, as a PNG (Discord can't show SVG). Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB. If you use a key, The Movie Database's terms of use apply to you.

### 4.7 Anime: sub or dub

Anime is anything Sonarr lists with the genre **Anime**. For anime, the request card asks **Dub** (English audio) or **Sub** (original audio with subtitles) before seasons. Every other show is unchanged.

**What Requestarr sets up in each Sonarr.** On every start (and when an instance is checked), Requestarr makes sure these exist, creating only what's missing:

| Item | Name |
|---|---|
| Tag | `requestarr-dub` |
| Tag | `requestarr-sub` |
| Release profile, limited to the dub tag | `Requestarr: anime dub` |
| Release profile, limited to the sub tag | `Requestarr: anime sub` |

It only ever looks at items with exactly these names. Your own tags and release profiles are never changed, and it's safe to restart as often as you like. If you change the terms in `provisioning.yml`, Requestarr updates its own two profiles to match.

A requested anime series is added with the tag for its language, so Sonarr applies that release profile to it. Your quality profile and custom formats still decide between the releases that pass.

**Default release terms.** Release groups mark dubbed anime in a few common ways; the defaults cover them:

| Profile | Must contain (any one) | Must not contain | Why |
|---|---|---|---|
| Dub | `Dual Audio`, `Dual-Audio`, `DualAudio`, `Multi-Audio`, `Multi Audio`, `Dubbed`, `English Dub`, `Eng Dub`, `Eng-Dub`, `EN Dub`, `[Dub]`, `(Dub)` | nothing | Most English-dubbed anime is released as **dual audio** (English and Japanese tracks), so dual and multi audio count as dub. The others are the usual dub markers. Without a must-contain list, Sonarr would happily grab the far more common sub-only releases. |
| Sub | nothing | `Dubbed`, `English Dub`, `Eng Dub`, `Eng-Dub`, `EN Dub`, `[Dub]`, `(Dub)` | Most anime releases are subtitled and say nothing about it, so there's nothing reliable to require. Dub-only releases are blocked. Dual audio is allowed, because it still has the original audio and subtitles. |

Matching isn't case-sensitive in Sonarr. To change the terms for one instance, add an `anime` block to it. A list you leave out keeps its default:

```yaml
  sonarr:
    - name: Sonarr
      url: http://sonarr:8989
      apiKey: ${SONARR_API_KEY}
      anime:
        enabled: true          # false: no Sub/Dub question on this instance
        priority: dub          # dub (default) | sub | first
        dub:
          required: [Dual Audio, Dual-Audio, Dubbed, English Dub]
        sub:
          ignored: [Dubbed, English Dub]
```

`anime` only works on Sonarr instances; on a Radarr it's a provisioning error, reported at start like any other. With `enabled: false` the card doesn't ask, no tag is added, and the tags and profiles aren't created on that instance.

**One language per series, and who wins.** A series exists once on a Sonarr, so it can only be one language there. The card says what an existing series is set to, and the instance's rule:

- **dub** (default): a dub request wins over sub.
- **sub**: a sub request wins over dub.
- **first**: the first request decides, and later requests can't change it.

A request for the losing language doesn't create a second request; the user follows the existing one and is told it stays in that language. A request for the winning language switches the series: Requestarr swaps the Sonarr tag (keeping every other tag on it), updates any open request for it, reposts the owner's and followers' messages saying so, and records an `anime.switch` entry in the audit log. Series that Requestarr never tagged are left alone.

**What a switch re-downloads.** Sonarr only applies a release profile when it searches. Episodes already downloaded stay as they are; they aren't deleted or replaced. New episodes, and missing ones when Sonarr searches for them, come in the new language. To replace existing episodes too, search for them in Sonarr yourself (for example **Series → Search Monitored**), knowing that it downloads them all again. Users are told this when they switch a series.

**Series type.** Anime is always added as Sonarr's **Anime** series type (absolute episode numbering), whatever `seriesType` the instance has; other shows use the instance's `seriesType`. If a series numbers better as Standard, switch it in Sonarr (**Edit → Series Type**). Requestarr never switches it back.

**Separate sub and dub instances.** If you'd rather keep dubbed and subtitled anime apart, run two Sonarr instances. Give one `priority: dub` and the other `priority: sub`, and point users at one with `/admin user access ... type:TV instance:{name}`. Then the same show can exist in both languages, once per instance. Two warnings:

- The instances need **different root folders**. Two Sonarrs sharing a folder fight over the same files.
- Point your media server at them as **separate libraries**. In one library, the show appears twice.

### 4.8 Downloads and availability

Once a request is sent, Requestarr follows it in Sonarr or Radarr and tells the user (and anyone following it) as it changes:

| State | When |
|---|---|
| ⬇️ **Downloading** | Sonarr or Radarr grabbed a release and it's in the download queue. |
| ⚠️ **Download stuck** | Sonarr, Radarr or the download client flags it (for example "stalled with no connections" or "no files eligible for import"), or its remaining size hasn't changed for 6 hours. The reason is shown. |
| ⚠️ **Download failed** | The download failed. Sonarr and Radarr usually blocklist that release and try another by themselves, so the request stays open. |
| 🎉 **Partly available** | TV only: some requested seasons are there, and the message says which. |
| 🎉 **Available** | Everything requested is there. For TV, a season counts as available when every episode that has aired has a file; seasons that haven't aired yet don't hold the request back. |

Each change replaces the request message at the bottom of the user's channel, with a mention, and moves the request's card to the bottom of the admin feed (section 5.4). The same state twice is never announced twice.

**How Requestarr hears about it.** Two ways, and they work together:

1. **Webhook:** Requestarr adds its own connection, named "Requestarr (availability)", to each Sonarr and Radarr through their own interface, so there's nothing to set up there. It's called on grab, import and upgrade, and when a download needs attention. Requestarr then asks the instance what changed, so a webhook can't report anything the instance doesn't confirm. Requestarr never changes or removes your own connections.
2. **Backup poll:** every 15 minutes Requestarr checks all open requests anyway, so a missed or unreachable webhook only delays the news.

The webhook needs the address Sonarr and Radarr use to reach Requestarr. Add it to `provisioning.yml`:

```yaml
availability:
  webhookUrl: http://requestarr:9797   # how Sonarr and Radarr reach Requestarr (include any URL base)
  pollMinutes: 15                      # backup poll; at least 5
```

Which address to use:

| Setup | `webhookUrl` |
|---|---|
| Docker, Sonarr/Radarr in the same Compose file or on the same Docker network | `http://requestarr:9797` (the service or container name, and the container port) |
| Docker, Sonarr/Radarr on another network or another machine | `http://{host address}:{published port}`, for example the Docker host's LAN IP and the port from `ports:` |
| Not in Docker, all on one machine | `http://localhost:9797` |
| Not in Docker, different machines | `http://{Requestarr machine's LAN address}:9797` |

If Requestarr has a URL base (`general.urlBase`, for example `/requestarr`), add it: `http://requestarr:9797/requestarr`.

Each instance's webhook address carries its own token, made from Requestarr's API key, so other callers can't post fake updates. If you change the API key, Requestarr updates the webhooks on the next start.

**When it can't reach Requestarr.** On every start, Requestarr asks each instance to test its webhook. If the test fails, **System → Status** shows a warning naming the instance and the address, and the backup poll covers it, so nothing is lost; updates just arrive at the next poll.

**Polling only.** Leave `webhookUrl` out to poll everything, or set `pollingOnly: true` on one instance (section 4.2). Requestarr then removes its webhook from that instance.

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

Requestarr registers exactly three commands:

- **`/request`** is visible to everyone. Its type list is filled in for each person from their own access, so someone without TV access never sees TV. A type also stays hidden until a Sonarr, Radarr or Lidarr for it has connected at least once (5.7).
- **`/help`** is visible to everyone. It opens user help, which only the asker can see, filtered to what they can use.
- **`/admin`** has three parts: `/admin user` opens a member's card, `/admin requests` goes through everything waiting on you one item at a time, and `/admin help` opens admin help. It is registered so that only members with **Manage Server** see it, which means ordinary users never do.

Everything else is a button, menu or short form: for users on their panel (5.5) and request cards; for admins on the admin panel, member and request cards, approval cards and the feed (5.4 to 5.10).

User help and admin help are separate. Admin help has its own topics, plus a **What users see** branch so you can walk someone through their help. User help never shows admin topics, even to an admin. Both are built from the same definitions as the commands and buttons, so they can't describe something that no longer exists.

**Optional:** co-admins who hold the Requestarr Admin role but not Manage Server won't see `/admin`. You can also keep `/admin` out of every channel except the admin channel. As the server owner:

1. Open **Server Settings → Integrations → (your Requestarr bot) → Manage**.
2. Under **Commands**, pick `/admin`.
3. Under **Roles & Members**, add the **Requestarr Admin** role and allow it.
4. Under **Channels**, turn off **All Channels** and allow only `#requestarr-admin`.

This is display only. Requestarr still refuses `/admin` from non-admins and outside the admin channel.

### 5.4 The admin channel and request channels

On first start, Requestarr creates these, or reuses them if they already exist. They live in two channel groups that Requestarr keeps (and recreates if they're deleted):

- **Requestarr Admin:** `#requestarr-admin` and `#requestarr-feed`. Only the Requestarr Admin role, the owner and the bot see the group, and both channels follow the group's permissions.
- **Requestarr Users:** `#requestarr-welcome` (5.12), `#requestarr-announcements` (5.8), `#requestarr-access-blocked` (5.12) and every member's private request channel. When the group reaches Discord's limit of 50 channels, new private channels go into "Requestarr Users 2", "3" and so on, with the same group permissions. The private channels keep their own permissions on top.

A server set up before this version is moved over on the next start: the "Requestarr Requests" group becomes "Requestarr Users" (renamed in place, so the private channels don't move), the admin channel, feed and announcements move into their groups, and nothing loses its messages.

- **`#requestarr-admin`:** run `/admin` here, and use the admin panel. Approval cards for requests, access requests (5.12) and media server access also appear here, and so do account events: new users, access changes, suspensions (including a member leaving the server), blocks and Plex link requests. Only the Requestarr Admin role, the owner, and the bot can see it. It's for admin actions only: admins make their own requests from their own request channel, like everyone (see 5.12).
- **`#requestarr-feed`:** the admin feed, next to the admin channel and just as private. It has one small card per request: thumbnail, title, type, who asked, the instance, the state and, for TV, the seasons and who follows it. Every change deletes the card and posts a fresh one at the bottom, so the newest activity is always last. If you delete the channel, Requestarr recreates it on the next start. The first time a feed channel exists, it's filled with cards for requests that are still waiting or in progress, not finished ones.
- **`#requestarr-announcements`** and the **Requestarr Members** role: news for every member, read-only for them (5.8).
You can rename the channels. Requestarr tracks them by ID, and puts a channel back in its group on the next start if it was moved out.

**Panels stay at the bottom.** Every panel (the admin panel, each member's panel, the welcome message and the blocked channel's message) is always the newest message in its channel, where people look: whenever Requestarr posts something else there, the panel moves below it a moment later, without pinging anyone. Panels aren't pinned, because they're always last anyway and every re-pin would add a "pinned a message" notice.

The feed card's buttons are only those valid now, and every click is checked again on the server:

| Button | Shown | What it does |
|---|---|---|
| **Approve**, **Deny**, **Already available** | while it waits for approval | the same as on the approval card; acting in either place updates both, and the user's message |
| **Cancel request** | while it waits, or once sent until something starts downloading | waiting: cancels it. Sent: cancels it and unmonitors the title in Sonarr or Radarr, so nothing more is searched for or downloaded. Nothing is deleted; the title stays there. |
| **Add seasons** | open TV requests | adds seasons for the requester (no approval, no limit) |
| **Delete** | once something is downloaded, and no removal is open | starts a removal (5.9) |
| **Force removal**, **Drop removal** | while a removal is open or waiting on you | Force deletes now (it asks first); Drop leaves the title |

The admin channel's panel has five buttons. Everything they open is visible only to you:

- **Pending queue (n):** the same as `/admin requests`, below. The number is how many items are waiting on you, and it updates by itself.
- **Users:** everyone with their status, channel and access, and a menu that opens their member card.
- **Add or find a member:** Discord's member picker. Pick anyone in the server to open their member card, and add them from there.
- **Remove a title:** searches every Sonarr and Radarr library, including titles nobody requested through Requestarr, and starts a removal (5.9).
- **Help:** admin help (the same as `/admin help`).

Requestarr posts the panel on first start and re-posts it if it is deleted.

**Everything waiting on you: `/admin requests`.** It takes no options. It shows one private card at a time, oldest first, with a counter ("3 of 7"), and goes through:

| Item | The card shows | Its buttons |
|---|---|---|
| A request waiting for approval | the full request card, how long it has waited, and for the requester and anyone following: their access for that type, how much of their limit they've used, and how many requests they have waiting | **Approve**, **Deny** (asks for a reason), **Already available**, **Cancel** (asks for an optional reason) |
| An approved request that failed or is stuck | the same, plus what went wrong in plain words and what you can do | **Try adding again** if it couldn't be added to Sonarr or Radarr, otherwise **Search again**; then **Already available** and **Cancel** |
| An access request from the welcome channel (5.12) | who asked, what they'd like, and their note | **Approve…** (choose their access), **Deny** (asks for an optional reason) |
| A Plex link someone asked for | who asked, their Plex username, and their access | **Approve Plex link**, **Deny Plex link** |
| A Sonarr or Radarr that can't be reached (5.7) | since when, and that requests for it are paused | **Check now** |

Every card also has **Skip**, and **Back** after the first. Acting on an item moves straight to the next one and says what happened at the top. If an item was already handled somewhere else in the meantime (on its approval card, for example), it's skipped and you're told so; nothing is done twice. At the end it says "Nothing left waiting on you", or, if you skipped some, how many still wait, with **Start over**.

**Search again** asks Sonarr or Radarr to search for the title again; the stuck download stays in your download client for you to remove there. The item stays in the list until the download moves on.

`/admin` and the admin panel work only in the admin channel (the feed's buttons work in the feed). Users' commands and panel buttons work only in their own channel.

### 5.5 Add your first user

In `#requestarr-admin`, press **Add or find a member** on the admin panel and pick the person (or run `/admin user` and pick them there). Their card says they aren't a Requestarr user yet; press **Add to Requestarr**.

Requestarr then:

1. Creates the Requestarr user from the member's Discord account.
2. Creates a role just for them (`requestarr-{name}`) and gives it to them.
3. Creates their private channel (`#requests-{name}`). Only that role, the admin role (and the owner), and the bot can see it.
4. Posts one message: the user's **panel**, which is also their welcome, and always the newest message in their channel. It greets them and lists what they may request, always matching their real access. It holds no how-to text, because instructions live in Help and kept text goes stale. Its buttons are:
   - **My requests:** their requests and the ones they follow.
   - **My limits:** their quota usage.
   - **Link Plex:** a short form for their Plex username, which sends you an access request. It shows only while a Plex server is connected to Requestarr, and disappears once their account is linked. Connecting a Plex server comes with Plex sharing in a later release, so for now this button isn't there.
   - **Help:** user help (the same as `/help`).

   The panel updates itself when you change their access or link their account. Existing channels get the new panel in place on the next start.

By default, a new user gets:

- **Movies and TV:** needs approval.
- **Music:** off.

The card then shows their access, ready to change (5.6).

Discord allows 250 roles per server, and every user needs one. Requestarr also uses three shared roles: Requestarr Admin, Requestarr Members and Requestarr Blocked (one role each, not one per person). Adding stops a few roles short of that limit and says so.

### 5.6 The member card: access, limits and more

`/admin user` (or **Users** or **Add or find a member** on the panel) opens a member's card, visible only to you. It shows their status, channel, access, limits, instance and media server account, and only the controls that apply to them now. Every change updates the card in place.

There are three access levels for each content type:

| Level | Meaning |
|---|---|
| off | The user can't request this type. |
| needs approval | Requests wait for an admin to approve them. |
| auto-approve | Requests go straight through. |

| On the card | What it does |
|---|---|
| **Movies access**, **TV access**, **Music access** | One menu per type, set to the current level. Pick another level and the card changes; nothing else is posted. Every type is shown to you, even one with nothing connected yet; granting such a type tells you privately that the member won't see it until an instance for it connects (5.7). |
| **Limits, instance, their requests…** | One menu for the rest: a type's limit (enter how many requests and per how many days, 7 if left empty; 0 removes the limit; following someone else's request never counts), which instance their requests go to (only when a type has several Sonarr or Radarr instances, 4.4), and **Their requests**. |
| **Link Plex…** / **Change Plex link…** | Links their Plex account directly. |
| **Approve Plex link** / **Deny Plex link** | Only while they've asked for Plex access themselves. |
| **Suspend** / **Unsuspend** | Suspend locks their channel: they can read it but can't post or request. Unsuspend unlocks it and gives their role back if they rejoined. |
| **Remove** | Asks first. Archives their channel (renamed `archived-…`, visible only to admins), deletes their role, and keeps their request history. Their card then offers **Add to Requestarr** to bring them back. |
| **Their requests** (in the menu above) | Their requests and the ones they follow, opening as request cards with the same buttons as their feed cards. |

Comics and books are not offered yet.

### 5.7 When Sonarr or Radarr can't be reached

Requestarr checks every Sonarr and Radarr once a minute. Each has one of three states:

| State | What users see |
|---|---|
| **Never connected** | Nothing. The type stays hidden from everyone, even members with access, until an instance for it answers for the first time. Admins can still grant it. |
| **Connected** | The type, if they have access. |
| **Lost** (it connected before, and stops answering) | The type as before. New requests for it say requests are paused right now. Nobody's access changes. |

When an instance has been unreachable for 3 minutes, one alert is posted in `#requestarr-admin` and pinned, and the instance is an item in `/admin requests` until it answers again. When it's back, the alert is unpinned and removed, and one more message (not pinned) says it's back and for how long it was down. Several outages at once each have their own pinned alert. A blip shorter than 3 minutes posts nothing.

Discord is only the working view: every outage is also kept in Requestarr's own record (which instance, when it started and ended, how long, and whether and by whom it was announced to users), for the admin web portal's outage history.

If it's still down after 30 minutes, the alert gets an **Announce to users** button. Users are told only if you press it (5.8).

Music stays hidden until Lidarr support arrives in a later version.

### 5.8 The announcements channel

`#requestarr-announcements` is for every member. Members can read it but can't post or start threads in it; admins (and the owner) can type their own announcements there directly, and the bot posts there. Everyone else in the server doesn't see it.

Members see it through one shared role, **Requestarr Members**. They get it when they're added or unsuspended and lose it when they're suspended or removed, the same way as their request channel. On every start, Requestarr makes sure every active member has the role and nobody else does, so existing members are added the first time this version starts. If the channel is deleted, it's recreated on the next start; if its permissions were changed, members are made read-only again.

What Requestarr posts there:

| When | In `#requestarr-announcements` | In `#requestarr-admin` |
|---|---|---|
| A Sonarr or Radarr is still down after 30 minutes and you press **Announce to users** on its alert | A plain notice, for example "TV requests are paused" and that the admin knows, pinned while the outage lasts. The button then goes away. | The alert says who announced it. |
| That outage ends | The notice is unpinned and removed, and "TV requests work again" is posted, automatically, only if it was announced. An outage you never announced is never mentioned to users. | The usual "is back" message. |
| Requestarr starts on a new version | A one-line card, for example "Requestarr 0.4.0 · 4 new things for you", with the main change in a few words. Nothing if a version has nothing for users. | A one-line card, for example "Requestarr 0.4.0 · 6 changes for admins, 4 for users". A version with nothing for either isn't posted. |

Every version card has **Read changes**: it shows the full card only to whoever pressed it, with the version and the day it was made as the title, then **For users** and **For admins** (only those with something in them), one line per change. Members see only their section. A long version is split into several cards between lines, never in the middle of one.

The "what's new" cards are a lasting record: each version is posted once, oldest first, even across restarts, and stays. The first time a server starts, it posts the full history from 0.1.0 on. A server that still has the older text posts gets them replaced by cards once, the history kept. The changes come from the project's changelog, written with every change in plain words (see [development](development.md#changelog)). Test servers keep them differently ([version posts](development.md#version-posts)).

### 5.9 Removing titles

Nothing leaves the library without asking the people who can watch it. A removal is of a whole title, or (TV) of chosen seasons.

**Starting one**

| Who | Where | Then |
|---|---|---|
| The requester | **Remove…** on their request message, once something of it is downloaded | Straight to the vote. |
| An admin, a requested title | **Delete** on its feed card, or **Remove a title** on the admin panel | The requester is asked first, in their channel: **Yes, remove it** starts the vote; **No, keep it** keeps it and the removal waits on you in `/admin requests`, where **Force removal** removes it anyway. No answer by the deadline counts as yes (the message says so), with a reminder on the reminder day. |
| An admin, a title nobody requested here | **Remove a title** on the admin panel: it searches every Sonarr and Radarr library, including titles never requested through Requestarr | Straight to the vote. A title whose requester was removed counts as nobody's. |

**The vote.** Every active user with access to that content type gets a **Still watching?** message in their own channel, with the title's card, which seasons, when the vote closes, and **Keep it**. Users who gain access while it's open get it too. One keep is enough: when the vote closes with a keep, the title stays; with none, it's removed. Users can take their own keep back while it's open. Nobody is told who pressed Keep it; you see who in `/admin requests`. Nothing is changed while a vote is open: the title stays monitored and its files stay.

**One at a time.** Only one removal can be open per title and season scope. The whole title overlaps every season, so a second removal of it, or of any of its seasons, is refused while one is open; removals of different seasons of the same show can run side by side.

**Removing.** The whole title: its files are deleted and it's removed from Sonarr or Radarr, **without** adding it to the exclusion list, so anyone can request it again later (as a new request). Seasons: those seasons' episode files are deleted and the seasons unmonitored; the show stays. The request (and everyone following it) is closed as removed, or loses those seasons.

**Force** is available at every stage: over a requester's No, over keeps, or to skip the vote altogether (**Force: delete now** when starting one). It always asks first, and the audit log records who and when.

**Outcomes.** Everyone who was asked gets their message reposted with the outcome (kept, removed, or removed by the admin), without buttons; so does the requester. The feed card follows the removal through every step: requester asked, voting (with the number of keeps), kept, removed, forced, cancelled. If Sonarr or Radarr refuses the deletion, nobody is told an outcome; the removal waits in `/admin requests`, and Force tries again.

**Settings.** In days, under `removal:` in `provisioning.yml` (each optional), and stored in Requestarr's settings so the admin web portal can change them:

```yaml
removal:
  voteDays: 7              # how long a vote is open
  requesterTimeoutDays: 7  # how long the requester has to answer an admin's removal
  reminderDay: 5           # the day of that the requester is reminded (0: no reminder)
```

### 5.10 Where everything is

For every command and button, see the [command reference](commands.md).

Users can also ask for media server access themselves with the **Link Plex** button on their panel (only while a Plex server is connected; see 5.5). An Approve / Deny card then appears in the admin channel, and their member card shows the same choice. Approving links the account and removes the Link Plex button from their panel. Plex invites are sent automatically in a later release.

### 5.11 When someone leaves the server

If a user leaves or is removed from the Discord server, Requestarr suspends them automatically and locks their channel. Nothing is deleted. If they rejoin, press **Unsuspend** on their card (`/admin user`): Requestarr gives their role back and unlocks the channel. The admin channel gets a note when someone leaves.

Every action in this section, whether an admin or Requestarr did it, is recorded in Requestarr's audit log.

### 5.12 The welcome channel and access requests

`#requestarr-welcome` is open to everyone in the server, and only Requestarr posts there (nobody can post or start threads). Its one message, a coloured card with a large heading and kept current, says what Requestarr is and how it works, with a **Request access** button. It's named with the `requestarr-` prefix so it can't clash with your server's own welcome channel.

**Request access** opens a short private form: what they'd like (only the types with a Sonarr or Radarr that has connected) and, after **Continue**, an optional note. Sending it:

- posts a card in `#requestarr-admin` with who asked, what they'd like and their note, and adds it to `/admin requests` (the panel's count goes up);
- **Approve…** on the card lets you choose their real access, one menu per type (starting from what they asked for), then **Add with this access** adds them exactly as **Add to Requestarr** does (5.5): role, private channel, panel. Nobody is added just by asking;
- **Deny** asks for an optional reason.

They get the outcome as a private message from the bot: on approval, where their new channel is; on a denial, your reason and when they can ask again. (If their Discord settings refuse private messages from the server, they see it the next time they press the button.)

Rules for the button:

- One open request per person. Pressing it again while one is open answers privately with its status and creates nothing.
- After a denial, they can ask again only after a cooldown (7 days by default, `accessRequests.denialCooldownDays`).
- Members who are already in are told where their channel is.
- Admins (the owner and anyone with the Requestarr Admin role) ask the same way and get their own request channel straight away: no card for you to decide, the types they picked on automatic approval and the rest off. Change their access later on their member card like anyone's.

**Media server buttons.** Next to **Request access** there's one button per media server you've connected (Plex, Jellyfin), and only those: with none connected, there are none. Someone can ask for media server access on its own, or tick it together with Requestarr access in the form; either way it reaches you exactly like a Link Plex request. Connecting a media server comes with Plex sharing in a later release, so for now there are no media server buttons.

**Spam protection.** Only clicks that can't do anything count (while a request is open, during a cooldown, or after being told their status), within the last hour; the click that opens the form never counts. Discord can't hide a shared button from one person, so Requestarr answers those clicks privately and does nothing else:

| Counted clicks | What happens |
|---|---|
| 5 within the hour | they wait 1 minute; every click meanwhile only says how long is left |
| 10 within the hour | they wait 24 hours |
| 5 more within 24 hours of the 10th | they're blocked |

Blocking gives them the **Requestarr Blocked** role. It hides `#requestarr-welcome` from them and shows `#requestarr-access-blocked`, a read-only channel whose one message tells them they can no longer request access through the bot and to message the server admin directly if they think it's wrong. You get one notice in `#requestarr-admin`. A block lasts until you lift it: **Unblock** on that notice or on their member card, or remove the role from them in Discord. Rapid clicking costs nothing: no messages to you, and one small record per person.

All of these are settings, with these defaults, under `accessRequests:` in `provisioning.yml` (each optional), stored so the admin web portal can change them later:

```yaml
accessRequests:
  denialCooldownDays: 7       # wait after a denial before asking again
  spam:
    windowMinutes: 60         # only clicks in the last hour count
    shortCooldownClicks: 5    # this many: wait shortCooldownMinutes
    shortCooldownMinutes: 1
    longCooldownClicks: 10    # this many: wait longCooldownHours
    longCooldownHours: 24
    blockClicks: 5            # this many more within blockWithinHours: blocked
    blockWithinHours: 24
```

### 5.13 The bot's picture and banner

Requestarr gives its bot the Requestarr picture and a banner, and updates them when a new release changes them, so you never upload them by hand. The picture is the same everywhere. The banner is the teal one, or the dev, QA or beta one on those test servers.

- It happens when the bot starts, and only when something differs. An unchanged picture is never uploaded again, so it stays well inside Discord's limit on how often a bot can change its picture. If Discord refuses anyway, the log says so and Requestarr tries again on a later start. The bot runs normally either way.
- It needs no permission and no change to the invite link: a bot changes its own picture and banner.
- **Your own picture stays.** Requestarr only replaces a picture or banner it set itself, or puts one in when the bot has none. It remembers Discord's id for each one it sets. If the id changes, someone changed it by hand, and Requestarr leaves it alone from then on. A picture the bot already had before Requestarr managed it counts as your choice too.
- **To let Requestarr replace your own,** set `takeOver: true`: Requestarr's picture and banner then always win, and one changed by hand is replaced on the next start.

Settings, under `botProfile:` in `provisioning.yml` (each optional), stored so the admin web portal can change them later:

```yaml
botProfile:
  keepUpToDate: true   # false: Requestarr never touches the bot's picture or banner
  takeOver: false      # true: replace one chosen by hand too (Requestarr's always wins)
```

### 5.14 Bug reports

`#requestarr-bug-reports`, in the Requestarr Users group, lets members and admins tell the people who build Requestarr about a problem. Nobody types in it: **Report a problem** opens a menu of Requestarr's parts, then a short form (what happened, what they expected). Requestarr adds your server's environment, its version, the time and the reporter's Discord name, nothing else from your server, and posts the report as a card in the channel, so you see every report.

**Filing on GitHub is off by default.** Turned on, each report also becomes an issue on Requestarr's public GitHub repository, which anyone can read; the form says so before anything is sent. It needs an access key of your own that can only read and write issues on that repository (GitHub: Settings, Developer settings, Fine-grained tokens, Issues "Read and write", nothing else). Leave it off unless you want your members' reports to be public. Requestarr's own test servers have it on.

```yaml
bugReports:
  fileIssues: false                    # true: each report becomes a public GitHub issue
  githubToken: ${GITHUB_ISSUES_TOKEN}  # in secrets.env, never here
  repository: RunemarkLabs/Requestarr
  reportsPerDay: 5                     # per person
  minutesBetween: 2                    # per person, between two reports
```

The limits stop the button flooding anything: someone who reaches them is told how long to wait, and nothing is posted.

## 6. Connect Plex (optional)

_Coming in Phase 3:_ Plex token and library-to-content-type mapping.

## 7. Lidarr (optional)

_Coming in Phase 2._

## Developing Requestarr

Working on Requestarr itself, or testing a build by hand? Two local stacks, each with test Sonarr and Radarr instances and its own Discord server and bot, run side by side: QA (the `qa` branch, for testing by hand) is started with `.\docker\dev.ps1 up` from PowerShell in the repo folder (or `docker/dev.sh up` with Git Bash), and dev (the `dev` branch, for automated testing) with `.\docker\dev.ps1 -Stack dev up`. Changes merge to `dev` first and reach `qa` only when promoted ([release flow](development.md#release-flow)). Their helpers stop and start a test instance, make a requested title look downloaded (`fake-download`), and make removal votes take minutes instead of days (`fast-votes on`). If PowerShell says running scripts is disabled, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once (or use `.\docker\dev.cmd`). Everything is in [development](development.md#docker-stack).

## Running the beta (staging) environment

For the project owner: an always-on Requestarr for early access testers, on its own Discord server (Requestarr Beta), whose requests really download and show up in your existing Plex, without any risk to your production Sonarr, Radarr or their files. It runs the published `staging` image: only versions you passed in QA ([release flow](development.md#release-flow)).

What runs: its own Requestarr, Sonarr and Radarr, all separate from production: own containers, own settings volumes, own ports, own Docker network (`requestarr-beta`), and two media folders of its own. They share only your download client and indexer manager, each beta download in a category of its own.

**How beta is kept away from production's files.** The beta Sonarr and Radarr see only the two beta media folders (as `/tv-beta` and `/movies-beta`) and the downloads folder. Production's media folders aren't mounted at all, so nothing in beta can read, change or delete them, and a beta removal vote can only ever delete beta files. Before Sonarr and Radarr start, a folder check refuses to start the stack unless both media folders are named `…-beta`, each holds a `.requestarr-beta` file you create once, they differ from each other, and neither is inside the downloads folder (nor it inside them). Requestarr's only root folders are `/tv-beta` and `/movies-beta`.

Do the steps in order; each ends with what to check before going on.

### B.1 What you need

- The server with Docker and Compose 2.17 or newer, and Git.
- Your download client and indexer manager (for example Prowlarr), as production uses them.
- Your Plex server.
- The Requestarr Beta Discord server, and a Discord application with a bot for it, set up as in [section 2](#2-create-your-discord-bot) (Server Members Intent on, invited with the same permissions). You need its bot token, application ID and the server's ID.
- The image: `ghcr.io/runemarklabs/requestarr` downloads without a login once the package is public on GitHub. While it's private, log Docker in on the server once with a GitHub token that can only read packages (GitHub → Settings → Developer settings → Personal access tokens → classic, **read:packages**): `docker login ghcr.io -u <your GitHub user>`, with the token as the password.

**Check:** `docker compose version` prints 2.17 or newer, and `docker pull ghcr.io/runemarklabs/requestarr:staging` downloads the image.

### B.2 Get the files

The beta stack is the `deploy/staging` folder of the repository. Put it in a folder of its own, outside any media folder:

```bash
git clone --depth 1 --branch staging https://github.com/RunemarkLabs/Requestarr.git requestarr-src
cp -r requestarr-src/deploy/staging requestarr-beta
cd requestarr-beta
```

**Check:** `ls` shows `compose.yaml`, `env.example`, `provisioning.yml`, `check-folders.sh`, `root-folders.sh` and `update.sh`.

### B.3 Make the beta media folders

Make two new, empty folders beside your production TV and movie folders (same disk, so Plex reads them the same way), named so they end in `-beta`. Create a `.requestarr-beta` file in each, which confirms the folder is for beta only, and give both to the user your production Sonarr and Radarr run as:

```bash
mkdir -p /path/to/media/tv-beta /path/to/media/movies-beta
touch /path/to/media/tv-beta/.requestarr-beta /path/to/media/movies-beta/.requestarr-beta
chown -R <media user>:<media group> /path/to/media/tv-beta /path/to/media/movies-beta
```

Never put the `.requestarr-beta` file in a production folder.

**Check:** `ls -la /path/to/media/tv-beta /path/to/media/movies-beta` shows the `.requestarr-beta` file in each, owned by your media user.

### B.4 Fill in the settings

```bash
cp env.example .env
chmod 600 .env
```

Open `.env` and fill in every value; the comments say what each is. In particular:

- **`BETA_TV_FOLDER`, `BETA_MOVIES_FOLDER`:** the two folders from B.3, full paths.
- **`BETA_DOWNLOADS_FOLDER`, `BETA_DOWNLOADS_PATH`:** the same downloads folder, and the same path inside the container, as your production Sonarr's compose file mounts (look for its downloads line, for example `/srv/downloads:/downloads`). Only the downloads folder: if production mounts one folder that holds both downloads and media (a single `/data`), mount just its downloads subfolder here. Imports then copy instead of hardlinking, which uses extra space only while a download is still seeding.
- **The three ports:** free ports, different from production's. Use beta's band, 19797, 18989 and 17878 (the standard ports with a 1 in front, so production keeps 9797, 8989 and 7878 and the first digit says which environment you're on). `ss -ltn | grep :<port>` prints nothing for a free port.
- **`PUID`, `PGID`:** the same as your production Sonarr and Radarr, so Plex can read what beta downloads.
- **`BETA_SONARR_API_KEY`, `BETA_RADARR_API_KEY`, `REQUESTARR_API_KEY`:** made up here, one each: `openssl rand -hex 16`.
- **`BETA_DISCORD_BOT_TOKEN`, `BETA_DISCORD_APPLICATION_ID`, `BETA_DISCORD_GUILD_ID`:** the beta bot and the Requestarr Beta server (B.1). This file is the only place they're kept.
- **`TMDB_API_KEY`:** optional, as in [4.6](#46-cast-from-the-movie-database-optional).

**Check:** `docker compose config --quiet` prints nothing. If it names a variable, fill that one in.

### B.5 Start it

```bash
docker compose up -d
```

**Check:**

- `docker compose ps -a` shows `requestarr`, `sonarr` and `radarr` running (Sonarr and Radarr healthy), and `folder-check` and `root-folders` exited with 0.
- `docker compose logs folder-check` ends with "folder check passed".
- `docker compose logs root-folders` shows `/tv-beta` and `/movies-beta` added (or already there).

If the folder check failed, nothing else started; its message says what's wrong. Fix `.env` and run `docker compose up -d` again.

### B.6 Connect the beta Sonarr and Radarr to your downloads

Open the beta Sonarr at `http://<server>:<BETA_SONARR_PORT>` and set a login when it asks. Then:

1. **Settings → Media Management → Root Folders:** only `/tv-beta` is there. Don't add another.
2. **Settings → Download Clients → +:** your download client, with the same host, port and login as in your production Sonarr, and **Category `tv-beta`**. If production uses remote path mappings, add the same ones. **Test**, then **Save**.
3. **Your indexer manager:** add the beta Sonarr as a new app. In Prowlarr: **Settings → Apps → + → Sonarr**, name it "Sonarr (beta)", Sonarr Server `http://<server>:<BETA_SONARR_PORT>`, API Key `BETA_SONARR_API_KEY` from `.env`, the rest as for production. **Test**, then **Save**. Prowlarr adds its indexers to the beta Sonarr.

Do the same in the beta Radarr (`http://<server>:<BETA_RADARR_PORT>`): root folder `/movies-beta` only, the download client with **Category `movies-beta`**, and "Radarr (beta)" as an app in Prowlarr.

If your download client needs categories made first (SABnzbd does; qBittorrent makes them on first use), add `tv-beta` and `movies-beta` there, each saving into its own folder inside the downloads folder. Beta and production downloads then never mix.

**Check:** in both: **System → Status** has no download client or indexer warnings, **Settings → Indexers** lists the indexers Prowlarr added, and **Settings → Connect** lists a "Requestarr" connection. Requestarr adds that one itself: it's the webhook that tells Requestarr about downloads. Sonarr and Radarr call it as `http://requestarr:9797` on the beta stack's own network, never through the server's ports, so there's nothing to set; **Test** on it should pass.

### B.7 Add the beta folders to Plex

Add each beta folder to the matching library you already have, so testers watch in the Plex they already use: **Plex → Settings → Manage → Libraries → (your TV library) → Edit → Add folders →** the `tv-beta` folder, then the same for `movies-beta` in your Movies library. If Plex runs in Docker, first mount the two beta folders into the Plex container (read-only is enough) and pick them by the path Plex sees.

**Check:** both libraries list the beta folder under their folders, and a library scan finishes without errors.

### B.8 Check Requestarr in Discord

Open the beta web portal at `http://<server>:<BETA_REQUESTARR_PORT>` and sign in with `REQUESTARR_ADMIN_USERNAME` and the password.

**Check:**

- **System → Status** has no Discord, provisioning, Sonarr, Radarr or webhook warnings.
- In the Requestarr Beta server: the bot is online, `#requestarr-admin` (in the Requestarr Admin group) has the admin panel at the bottom, and `#requestarr-welcome` (in the Requestarr Users group) shows the welcome message with **Request access**.
- `#requestarr-admin` shows "what's new" for every version from 0.1.0 on, oldest first, and `#requestarr-announcements` the members' part. On beta these posts stay for good: each new version is added once.

### B.9 A first real request

Add yourself as a member from the admin panel ([5.5](#55-add-your-first-user)), then request a small movie with `/request` in your request channel.

**Check:** in your download client the download has the category `movies-beta`; when it finishes, the files are in your `movies-beta` folder (not your production movies folder); the request message says it's ready to watch; and after Plex's next scan the movie is in your Movies library.

### B.10 Testers

Add the testers from the admin panel's member picker ([5.5](#55-add-your-first-user)), once they've joined the Requestarr Beta server. Tell them about the known limitation below.

### Updating the beta

When a new version reaches staging, update with one command in the beta folder:

```bash
./update.sh
```

It pulls the newest `staging` image. If it's newer than what runs, it stops Requestarr, backs up its data to `backups/`, and starts the new version (Sonarr and Radarr are left alone). It prints the old and new versions.

**Check:** it ends with "Requestarr updated: 0.3.0 -> 0.3.1" (your versions), and `#requestarr-admin` gets the new version's "what's new".

To go back to the version that ran before the last update, with the data as it was then:

```bash
./update.sh rollback
```

Anything done in Discord since that update (new requests, votes) is lost with the rollback. It pins that version (`REQUESTARR_TAG` in `.env`), so a restart keeps it; the next `./update.sh` returns to the newest `staging` image. `./update.sh status` shows which version runs and what a rollback would go back to.

**Automatic updates:** not recommended. Promotions to staging happen when you say "QA passed", so you know when to run it, and an update tool such as Watchtower would skip the backup that makes a rollback safe. If you'd rather not think about it, a nightly cron job is safe, because `./update.sh` does nothing when there's no new version: `0 4 * * * cd /path/to/requestarr-beta && ./update.sh >> update.log 2>&1`.

### Known limitation: beta can download a second copy

Beta only knows what the beta Sonarr and Radarr have. A title that's already in your production Plex can still be requested on beta, and it downloads a second copy into the beta folders. Plex shows both copies under one title (as two versions). Tell testers to check Plex first. Removing it on beta deletes only the beta copy.

### Taking the beta down

`docker compose down` stops it and keeps everything. `docker compose down -v` also deletes the beta Requestarr's, Sonarr's and Radarr's settings. The beta media folders are yours to delete; remove them from the Plex libraries first.

## 8. Troubleshooting

- **"Private application cannot have a default authorization link":** set Installation → Install Link to None first.
- **The bot cannot create or assign roles:** move the bot's role higher in Server Settings → Roles.
- **Members leaving are not detected:** Server Members Intent is off.
- **"Only the Requestarr admin can use this command":** you are not the server owner and don't have the role set in `adminRoleId`.
- **"Admin commands only work in the Requestarr admin channel":** run `/admin` and use the admin panel in `#requestarr-admin`, not in a user's channel.
- **"Backend check failed" under System → Status:** the message names the instance and the cause: wrong URL or API key, an unsupported version (Radarr 5+ and Sonarr 4+ only), or a quality profile or root folder that doesn't exist in that instance. Fix `provisioning.yml` and restart.
- **Users don't see /admin:** that's intended; it only shows to members with Manage Server (see 5.3).
- **System → Status warns about Pin Messages:** the bot's role lacks **Pin Messages**, so outage notices are posted but not pinned while an outage lasts. Everything else keeps working. Turn it on in Server Settings → Roles (see section 2, step 5).
- **The /request type list is empty, or a type is missing:** that user has the type set to off (change it on their card, `/admin user`), or no instance for it has connected yet (5.7).
- **Old admin commands such as `/admin user add` or `/admin approve` still show:** Discord caches commands; press Ctrl+R in the Discord app. They were replaced by `/admin user`, `/admin requests` and the panel.
- **Discord still shows old commands after an update:** press Ctrl+R in the Discord app, or fully quit it from the system tray and reopen it.
- **The bot doesn't come online:** check **System → Status** for the Discord message. Common causes are a wrong token, the bot not being invited to the server in `guildId`, or a missing application ID.
- **"Provisioning file could not be applied" under System → Status:** the message names the problem, for example an environment variable that is not set or an invalid value. Fix `provisioning.yml` or `secrets.env`, then `docker compose restart requestarr`.
- **"can't reach Requestarr at …" under System → Status:** that Sonarr or Radarr can't call Requestarr's webhook. Downloads are still tracked by the backup poll. Check `availability.webhookUrl` against the table in section 4.8, then restart Requestarr. On Docker, Requestarr and the instance must share a network for `http://requestarr:9797` to work.
- **The container restarts or stays unhealthy:** check `docker compose logs requestarr`. A common cause is a `./config` folder the container cannot write to (see section 3.2).
