# Requestarr command reference

Requestarr registers exactly three Discord commands:

- **`/request`** for everyone.
- **`/help`** for everyone.
- **`/admin`** for admins only: `/admin user`, `/admin requests` and `/admin help`. It's hidden from members without Manage Server.

Everything else is a button, on a panel or on a card. Each panel is always the newest message in its channel; when Requestarr posts something there, the panel moves below it.

- **Users** work in their own private request channel.
- **Admins** work in `#requestarr-admin`. Admins are the server owner and anyone holding the Requestarr Admin role.

Requestarr checks who you are and where you are on every command and button click. Anything used elsewhere is refused with a short explanation.

## For users

### `/request {type} {query}`

| Option | What to enter |
|---|---|
| `type` | Pick from the list. It only shows types you're allowed to request, for example Movies or TV. |
| `query` | A title to search for, or an ID: `tmdb:603` for a movie, or `tvdb:12345` for a TV show. |

A request is **one message** that changes in place as you go: results, details, seasons, a confirmation, then the final status. Nothing else is posted for it, so separate requests stay separate.

**Results** (up to 10). Each line shows the title with its year, then the year, the network or studio, and either:

- the top three cast, when the admin has set a The Movie Database key, for example `2024 · Apple TV · Joel Edgerton, Jennifer Connelly, Alice Braga`
- the genre, without a key, for example `2024 · Apple TV · Drama`

Pick one to see its card.

**The card:**

- a large poster
- genres, rating, age rating, and runtime
- movies: studio, status, and cinema, digital and physical release dates
- TV: network, status, number of seasons (and whether there are specials), and first and last air dates
- the top cast and the episode count, with a The Movie Database key
- which instance the request goes to
- the overview, and a link to IMDb

The card also says if the title is:

- **Already in the library:** nothing to request.
- **Already requested by someone:** press **Follow request**. You'll be notified with them, and it doesn't use your quota.
- **Previously denied:** shows the reason. You can request it again.
- **Your own pending request:** has a **Cancel request** button.

**Back to results** returns to the list.

**Making the request:**

- **Movies:** press **Request**.
- **TV:** pick one of two paths:
  - **All seasons:** requests every season now, and new seasons as they air, straight away. Specials aren't included.
  - **Choose seasons:** tick seasons in the list, including **Specials** if you want them. Shows with more than 24 seasons have pages: use **◀ / ▶** to move between them, and your ticks are kept. The message shows everything picked so far. **Also new seasons** turns monitoring of future seasons on or off. Press **Continue**, and Requestarr says what you chose in plain words, for example "seasons 1–2 and 36 only; new seasons won't be added". Press **Request** to send it, or **Back** to change your picks.

**Anime: Dub or Sub.** For anime (Sonarr's genre **Anime**), the card first asks **Dub** (English audio) or **Sub** (original audio with subtitles), then the usual season choice. Every step after that shows the language, and so does the final status.

- A series is one language on the server. The card says what an existing series is set to, and the server's rule: dub wins, sub wins, or the first request decides.
- If the series is already set to the other language and yours wins, the card shows **Switch to Dub** (or **Switch to Sub**). Switching tells everyone on the request. Episodes already downloaded stay as they are; new and missing ones come in the new language.
- If yours loses, you follow the existing request, and the status says which language it stays.

**The final status** is always one of:

| Status | Meaning |
|---|---|
| ✅ **Request sent** | Added to Sonarr or Radarr; a search has started. |
| ⏳ **Waiting for approval** | Waiting for an admin. Has a **Cancel request** button until it's decided. |
| 📌 **Following** | Someone else asked first; you're notified with them. |
| 🎉 **Already available** | Good news: it's already in the library, ready for you. |
| ⛔ **Not requested** | Refused, with the reason (for example your limit). |

For TV, the status repeats which seasons were requested.

**After it's sent**, the message follows the download: ⬇️ **Downloading**, ⚠️ **Download stuck** (with the reason), ⚠️ **Download failed** (Sonarr or Radarr usually tries another release by itself), 🎉 **Partly available** (TV: which seasons are there), and 🎉 **Available** when it's ready to watch. A TV season counts as available when every episode that has aired is there.

**Add seasons** (TV): on the request's message, at any time, also on requests you follow. Pick from the seasons not requested yet (pages for long shows), and optionally **Also new seasons**, then press **Add these seasons**. No approval and no limit. Sonarr starts searching for them straight away; on a request still waiting for approval they go to Sonarr with it. Seasons are never removed here.

**After the admin decides**, your old request message is replaced by a fresh one at the bottom of your channel that mentions you, so you get a ping: ✅ **Approved**, 🎉 **Already available**, ⛔ **Denied** (with the reason), or 🚫 **Cancelled**. It only has the buttons that still apply. The same happens if an anime series is switched to another language, and to the messages of anyone following the request. Nothing else is posted, so the outcome isn't repeated.

- Your own **Cancel request** changes the message in place, without a new message.
- A message you're still working through (results, seasons, confirmation) is never moved.
- If the old message can't be deleted, it just loses its buttons.
- If there's no message to replace (it was deleted, or the request is from before this version), you get a separate message instead.

**Buttons clicked late are safe.** Every button re-checks the request first. If it has changed (approved, denied, cancelled, or requested by someone else meanwhile), nothing happens; you're told privately why, and the message is refreshed to show the current state. **Follow request** only ever follows; it never creates a new request.

### Removing and keeping titles

- **Remove…** on your own request message, once something of it is downloaded: asks to remove it (TV: the whole show or chosen seasons). Everyone else who can watch it gets a vote.
- **Still watching?** messages come to your channel when a title you can watch may be removed. **Keep it** keeps it (one keep is enough, nobody is told who pressed it); **Take back my keep** while the vote is open. Nothing changes until it closes.
- **Remove your request?** comes when the admin wants to remove something you requested: **Yes, remove it** or **No, keep it**; no answer by the date shown counts as yes.
- Everyone asked gets the outcome. A removed title can be requested again any time.

### Your panel

One message, always the newest in your channel. It welcomes you and shows what you can request, always matching your access.

| Button | What it does |
|---|---|
| **My requests** | Lists your requests and the ones you follow (only you see the reply). |
| **My limits** | Shows your access level and quota usage per type. |
| **Link Plex** | Opens a form for your Plex username and sends an access request to the admin. It's there only while the admin has a Plex server connected, and disappears once your account is linked. |
| **Help** | Opens help, the same as `/help`. |

### `/help`

Opens help that only you can see. It starts with a short overview, then you pick topics from a menu, with **Back** to return. Topics:

- requesting and searching by ID
- TV seasons
- anime: sub or dub
- following an existing request
- downloads and availability
- my requests and cancelling
- limits
- linking Plex
- removing and keeping titles
- announcements

You only see topics you can use: no seasons or anime topic without TV access, no Plex topic once you're linked.

## For admins

There are three admin commands, all under `/admin`, visible only to members with Manage Server, and working only in `#requestarr-admin`. Everything else is a button, menu or short form on the admin panel, on the cards these commands open, on approval cards and on the admin feed. Every click is checked again on the server; what you see is only a hint.

### `/admin user member:@someone`

Opens that member's card, visible only to you, with only what applies to them now. Every change updates the card in place.

| Member | The card offers |
|---|---|
| Not a Requestarr user (or removed) | **Add to Requestarr**: creates their role, private channel and panel. Movies and TV start as needs approval, Music off. |
| Active | One access menu per type (**Movies access**, **TV access**, **Music access**: off, needs approval, auto-approve, set to the current level); **Limits, instance, their requests…** (a type's limit: requests per window and days, 0 removes it; which instance their requests go to, only when a type has several; **Their requests**); **Link Plex…**, **Suspend**, **Remove** (asks first). Granting a type with nothing connected yet says privately that they won't see it until an instance connects. |
| Suspended | The same, with **Unsuspend** instead of Suspend. Unsuspend gives their role back if they rejoined. |
| Waiting for a Plex link they asked for | Also **Approve Plex link** and **Deny Plex link**. |

### `/admin requests`

Goes through everything waiting on you, one private card at a time, oldest first, with a counter ("3 of 7"). No options.

| Item | Buttons |
|---|---|
| Request waiting for approval | **Approve**, **Deny** (asks for a reason), **Already available**, **Cancel** (asks for an optional reason) |
| Approved request that failed or is stuck | **Try adding again** (it couldn't be added) or **Search again** (it failed or stalled downloading), **Already available**, **Cancel** |
| Access request from the welcome channel | **Approve…** (choose their access, then **Add with this access**), **Deny** (asks for an optional reason) |
| Plex link request | **Approve Plex link**, **Deny Plex link** |
| Sonarr or Radarr that can't be reached | **Check now** |

Every card has **Skip**, and **Back** after the first. Request cards show the full card, and for the requester and each follower their access for that type, their limit used and how many requests they have waiting. Problem items say in plain words what went wrong and what you can do. Acting moves straight to the next item. Items handled elsewhere meanwhile are skipped. It ends with "Nothing left waiting on you", or with **Start over** if you skipped some. **Cancel** on a request that was already sent unmonitors the title in Sonarr or Radarr, so nothing more is searched for; it's never deleted.

A member's own requests open as request cards from **Their requests** on their card, with the same buttons as the feed card; the card changes in place when you act on it. A request whose title is already in the library by the time you act always ends as **already available**: approving doesn't add it again, and denying or cancelling it says "already available" instead. A late click changes nothing: you're told why, and the card shows the request as it is.

The admin channel is for admin actions, not your own requests: `/request …` there only points you to your own request channel. To get one, press **Request access** in `#requestarr-welcome` like everyone else; as an admin you're added straight away, with the types you pick on automatic approval and the rest off, and your requests are made from your channel like any member's. Request messages already posted in the admin channel keep their buttons.

### `/admin help`

Admin help, only you can see it, in `#requestarr-admin` only. Topics:

- members: adding and managing
- access requests and the welcome channel: the form, approving and denying, the cooldown, spam protection and blocks
- approvals and the queue
- the admin feed
- removing titles
- announcements and outages
- downloads, webhooks and polling
- limits
- anime, sub/dub and priority: the Sonarr tags and release profiles, `anime.priority`, and separate sub and dub instances
- media server requests
- channel groups and panels
- the admin role and command visibility
- **What users see**: the full user help, so you can walk someone through theirs

`/help` always shows user help, even to an admin.

### Admin panel

The newest message in `#requestarr-admin`. What it opens is visible only to you.

| Button | What it does |
|---|---|
| **Pending queue (n)** | The same as `/admin requests`; n is how many items wait on you, kept up to date. |
| **Users** | Every user with their status, channel and access; a menu opens their member card. |
| **Add or find a member** | Discord's member picker: pick anyone in the server to open their member card and add them from there. |
| **Remove a title** | Searches every Sonarr and Radarr library (titles nobody requested too); pick one to start its removal. |
| **Help** | Opens admin help, the same as `/admin help`. |

### Approval cards and the admin feed

Requests that need approval post a card in `#requestarr-admin` with **Approve**, **Deny** and **Already available**. When a request is approved, denied or cancelled, the card changes to show it and loses its buttons, and the user's request message is reposted at the bottom of their channel with a mention (see the user section).

`#requestarr-feed` has one small card per request, moved to the bottom on every change: new requests, someone following, approved, denied, cancelled, already available, downloading, stuck or failed, available. Its buttons are the same as the request card's. Your reply comes privately. Account events stay in `#requestarr-admin`.

Media server access requests post a card here with **Approve** and **Deny**; the member's card offers the same.

### Removals

| Where | Button | What it does |
|---|---|---|
| Feed card of a downloaded title | **Delete** | Opens its removal card: TV can pick the whole show or seasons; **Start removal** asks the requester first (or goes to the vote if nobody requested it); **Force: delete now** asks, then deletes. |
| Feed card while a removal is open | **Force removal**, **Drop removal** | Force asks, then deletes now; Drop leaves the title (and tells everyone asked that it was called off). |
| `/admin requests` | **Force removal**, **Drop removal** | On a requester's No, a vote kept by keeps (shows who pressed Keep it), a deletion Sonarr or Radarr refused, and a removal still waiting on its requester (shows the deadline). |

Deleting removes the files and the title from Sonarr or Radarr without excluding it; season removals delete those seasons' files and unmonitor them. Settings: `removal.voteDays` (7), `removal.requesterTimeoutDays` (7) and `removal.reminderDay` (5).

### Announcements

`#requestarr-announcements` is read-only for every member (through the shared **Requestarr Members** role) and admins post in it directly.

- **Announce to users** appears on a Sonarr or Radarr down alert once it has been down for 30 minutes. It posts a plain notice ("TV requests are paused…") and the recovery is then announced automatically. Never pressed: users never hear about it.
- While an outage lasts, its alert in `#requestarr-admin` (and the notice, if announced) is pinned; when it's over both are removed and the "back" messages posted.
- On a new version, a one-line card goes to `#requestarr-admin` ("6 changes for admins, 4 for users") and one to `#requestarr-announcements` ("4 new things for you", with the main one), once per version. **Read changes** on either shows the full card, only to whoever pressed it.

### The welcome channel

`#requestarr-welcome` is open to everyone in the server; only Requestarr posts there.

| Button | Who | What it does |
|---|---|---|
| **Request access** | anyone | A private form: the types they'd like (only connected ones), then **Continue** and an optional note. Sends the admin a card. One open request per person; after a denial, they can ask again after the cooldown. Members already in are told where their channel is. Admins get their own channel straight away, with what they picked on automatic approval. |
| **Plex access**, **Jellyfin access** | anyone | Only for media servers the admin has connected (none yet). Asks for media server access like Link Plex. |

Clicks that can't do anything are answered privately; repeated ones make the person wait (5 within an hour: 1 minute; 10: 24 hours) and 5 more within 24 hours block them: the **Requestarr Blocked** role hides the welcome channel and shows `#requestarr-access-blocked`. **Unblock** on the admin notice or their member card lifts it, as does removing the role.

### Where the old admin commands went

| Before | Now |
|---|---|
| `/admin user add` | **Add or find a member** on the panel, or `/admin user`: **Add to Requestarr**, then the access menus |
| `/admin user access` | Member card: the type's access menu, and **Limits, instance, their requests…** for the instance |
| `/admin user quota` | Member card: **Limits, instance, their requests…** |
| `/admin user info` | `/admin user` (the card itself), or **Users** on the panel |
| `/admin user suspend` / `unsuspend` | Member card: **Suspend** / **Unsuspend** |
| `/admin user remove` | Member card: **Remove** (asks first) |
| `/admin user mediaserver` | Member card: **Link Plex…** |
| `/admin queue [user] [type]` | `/admin requests`, or **Pending queue** on the panel; **Their requests** on a member card for one person |
| `/admin approve` / `deny` / `available` / `cancel` | The buttons in `/admin requests`, on the approval card, or on the feed card |
| `/admin mediaserver approve` / `deny` | The access-request card's buttons, or the member card's **Approve Plex link** / **Deny Plex link** |
| `/admin help` | unchanged |

## Limits

Requestarr keeps within Discord's limits:

- At most 10 search results.
- At most 25 menu options.
- Labels of at most 100 characters.
- Messages of at most 2,000 characters.
- Button and menu IDs of at most 100 characters.

## The Movie Database

Cast and TV episode counts come from The Movie Database when the admin configures a key (setup guide 4.6). Wherever cast appears, Requestarr shows a short "Cast from TMDB" credit with TMDB's small logo in the card footer. While a key is set, `/help` and `/admin help` have a **Credits** topic with TMDB's logo and its notice: Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.
