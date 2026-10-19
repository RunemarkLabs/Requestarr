# Requestarr command reference

Requestarr registers exactly two Discord commands:

- **`/request`** for everyone.
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

Searching shows up to 10 results. Pick one to see its card, with poster, year and overview.

**What the card shows:**

- **Already in the library:** nothing to request.
- **Already requested by someone:** press **Follow request**. You'll be notified with them, and it doesn't use your quota.
- **Previously denied:** shows the reason. You can request it again.
- **Your own pending request:** has a **Cancel request** button.

**Making the request:**

- **Movies:** press **Request**.
- **TV:** pick seasons: specific ones, **All seasons**, and optionally **Future seasons**, which keeps monitoring new seasons as they air. Very long shows list the 23 most recent seasons.

What happens next depends on your access for that type:

- **Needs approval:** waits for an admin. The reply has a **Cancel request** button until it's decided.
- **Auto-approve:** added straight away.

The outcome is always posted in your channel.

### Your panel

A pinned message at the top of your channel. It shows what you can request.

| Button | What it does |
|---|---|
| **My requests** | Lists your requests and the ones you follow (only you see the reply). |
| **My limits** | Shows your access level and quota usage per type. |
| **Link Plex** | Opens a form for your Plex username and sends an access request to the admin. It disappears once an account is linked. |

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
| `/request …` | In the admin channel, makes a request as yourself. Admin requests skip approval and quotas and use the default instance. |

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

## Limits

Requestarr keeps within Discord's limits:

- At most 10 search results.
- At most 25 menu options.
- Labels of at most 100 characters.
- Messages of at most 2,000 characters.
