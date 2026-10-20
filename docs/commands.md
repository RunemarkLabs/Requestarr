# Requestarr command reference

Requestarr registers exactly three Discord commands:

- **`/request`** for everyone.
- **`/help`** for everyone.
- **`/admin`** for admins only. It's hidden from members without Manage Server.

Everything else is a button, on a pinned panel or on a card.

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

**The final status** is always one of:

| Status | Meaning |
|---|---|
| ✅ **Request sent** | Added to Sonarr or Radarr; a search has started. |
| ⏳ **Waiting for approval** | Waiting for an admin. Has a **Cancel request** button until it's decided. |
| 📌 **Following** | Someone else asked first; you're notified with them. |
| ✅ **Already available** | It's in the library. |
| ⛔ **Not requested** | Refused, with the reason (for example your limit). |

For TV, the status repeats which seasons were requested.

**After the admin decides**, the request message itself changes to show it: ✅ **Approved**, ✅ **Already available**, ⛔ **Denied** (with the reason), or 🚫 **Cancelled**. Buttons that no longer apply disappear. You still get a separate message that pings you. Requests made before this version just get the separate message.

**Buttons clicked late are safe.** Every button re-checks the request first. If it has changed (approved, denied, cancelled, or requested by someone else meanwhile), nothing happens; you're told privately why, and the message is refreshed to show the current state. **Follow request** only ever follows; it never creates a new request.

### Your panel

One pinned message at the top of your channel. It welcomes you and shows what you can request, always matching your access.

| Button | What it does |
|---|---|
| **My requests** | Lists your requests and the ones you follow (only you see the reply). |
| **My limits** | Shows your access level and quota usage per type. |
| **Link Plex** | Opens a form for your Plex username and sends an access request to the admin. It disappears once an account is linked. |
| **Help** | Opens help, the same as `/help`. |

### `/help`

Opens help that only you can see. It starts with a short overview, then you pick topics from a menu, with **Back** to return. Topics:

- requesting and searching by ID
- TV seasons
- following an existing request
- my requests and cancelling
- limits
- linking Plex

You only see topics you can use: no seasons topic without TV access, no Plex topic once you're linked.

## For admins

All admin commands are under `/admin` and only work in `#requestarr-admin`.

### Users

| Command | What it does |
|---|---|
| `/admin user add member:@someone [movie] [tv] [music]` | Adds a member. Creates their role, private channel and panel, and sets their access. Defaults: Movies and TV need approval; Music is off. |
| `/admin user access member:@someone type:{Movies\|TV\|Music} level:{Off\|Needs approval\|Auto-approve} [instance:{name}]` | Changes one content type. `instance` sends that user's requests of that type to a specific Sonarr or Radarr. The user's panel updates. |
| `/admin user quota member:@someone type:{…} limit:{n} [days:{n}]` | Limits requests per rolling window (default 7 days). `limit:0` removes the limit. |
| `/admin user info member:@someone` | Shows status, channel, access levels, quotas, and the linked media server account. |
| `/admin user suspend member:@someone` | Locks their channel. |
| `/admin user unsuspend member:@someone` | Unlocks it. If they rejoined the server, their role is given back. |
| `/admin user remove member:@someone` | Archives their channel, deletes their role, and keeps their history. |
| `/admin user mediaserver member:@someone service:Plex username:{name}` | Links their Plex account. Their Link Plex button disappears. |

### Requests

| Command | What it does |
|---|---|
| `/admin queue [user] [type]` | Lists requests waiting for approval. |
| `/admin approve id:{number}` | Approves a request and sends it to the user's Sonarr or Radarr. The **Approve** button on a request card does the same. |
| `/admin deny id:{number} [reason]` | Denies a request. The reason is shown to the user. The **Deny** button asks for a reason. |
| `/admin available id:{number}` | Closes a pending request as **already available**, for when you already have the title. The **Already available** button on a request card does the same. |
| `/admin cancel id:{number} [reason]` | Cancels a pending request for the user, for example a duplicate. The reason is shown to the user. |

A request whose title is already in the library by the time you act always ends as **already available**: approving doesn't add it again, and denying or cancelling it says "already available" instead.

When a request is approved, denied or cancelled, the user's request message and the approval card here both change to show it, and their buttons disappear. Clicking **Approve** or **Deny** on a card that's already decided changes nothing: you're told why, and the card is refreshed.
| `/request …` | In the admin channel, makes a request as yourself. Admin requests skip approval and quotas and use the default instance. |

### `/admin help`

Admin help, only you can see it, in `#requestarr-admin` only. Topics:

- onboarding and access
- approvals and the queue
- limits
- media server requests
- the admin role and command visibility
- **What users see**: the full user help, so you can walk someone through theirs

`/help` always shows user help, even to an admin.

### Media server access

| Command | What it does |
|---|---|
| `/admin mediaserver approve id:{number}` | Approves a user's access request: links the account. Plex invites follow in a later release. |
| `/admin mediaserver deny id:{number}` | Denies it. |

The **Approve** and **Deny** buttons on the access-request card do the same.

### Admin panel

Pinned in `#requestarr-admin`.

| Button | What it does |
|---|---|
| **Pending queue** | Lists requests waiting for approval. |
| **Users** | Lists every user with their status, channel and access. |
| **Help** | Opens admin help, the same as `/admin help`. |

## Limits

Requestarr keeps within Discord's limits:

- At most 10 search results.
- At most 25 menu options.
- Labels of at most 100 characters.
- Messages of at most 2,000 characters.
- Button and menu IDs of at most 100 characters.

## The Movie Database

Cast and TV episode counts come from The Movie Database when the admin configures a key (setup guide 4.6). Wherever cast appears, Requestarr shows a short "Cast from TMDB" credit with TMDB's small logo in the card footer. While a key is set, `/help` and `/admin help` have a **Credits** topic with TMDB's logo and its notice: Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB.
