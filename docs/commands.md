# Requestarr command reference

Every Discord command Requestarr registers.

- **Users** run commands in their own private request channel.
- **Admins** run commands in `#requestarr-admin`. Admins are the server owner and anyone holding the Requestarr Admin role.

Requestarr checks who you are and where you are on every command and every button click. Commands run anywhere else are refused with a short explanation.

## For users

| Command | What it does |
|---|---|
| `/request movie query:{title}` | Searches for a movie. Pick a result to see its card (poster, year, overview), then press **Request**. |
| `/request tv query:{title}` | Searches for a TV show. Pick a result, then pick seasons: specific ones, **All seasons**, and optionally **Future seasons**, which keeps monitoring new seasons as they air. |
| `/request movie-tmdb id:{number}` | Opens the card for a movie by its TMDB ID. |
| `/request tv-tvdb id:{number}` | Opens the card for a show by its TVDB ID. |
| `/request cancel id:{number}` | Cancels one of your requests that is still waiting for approval. |
| `/requests [status] [type]` | Lists your requests and the ones you follow. |
| `/quota` | Shows your access level and how much of each limit you've used, per type. |
| `/mediaserver request username:{plex username}` | Asks the admin for access to the media server. |

What you see on a title's card:

- **Already in the library:** nothing to request.
- **Already requested by someone:** press **Follow request**. You're notified with them, and it doesn't use your quota.
- **Previously denied:** shows the reason. You can request it again.

What happens after you request depends on your access level for that type:

- **Off:** refused.
- **Needs approval:** waits for an admin.
- **Auto-approve:** added straight away.

Either way, the outcome is posted in your channel.

## For admins

The admin-only commands below are hidden from members without Manage Server. To show them to co-admins, see setup guide section 5.3.

### Users

| Command | What it does |
|---|---|
| `/user add member:@someone [movie] [tv] [music]` | Adds a member. Creates their role and private channel and sets their access. Defaults: Movies and TV need approval; Music is off. |
| `/user access member:@someone type:{Movies\|TV\|Music} level:{Off\|Needs approval\|Auto-approve} [instance:{name}]` | Changes one content type. `instance` sends that user's requests of that type to a specific Sonarr or Radarr. |
| `/user quota member:@someone type:{…} limit:{n} [days:{n}]` | Limits requests per rolling window (default 7 days). `limit:0` removes the limit. |
| `/user info member:@someone` | Shows status, channel, access levels, quotas, and the linked media server account. |
| `/user suspend member:@someone` | Locks their channel. |
| `/user unsuspend member:@someone` | Unlocks it. If they rejoined the server, their role is given back. |
| `/user remove member:@someone` | Archives their channel, deletes their role, and keeps their history. |
| `/user mediaserver member:@someone service:Plex username:{name}` | Links their Plex account. |

### Requests

| Command | What it does |
|---|---|
| `/queue [user] [type]` | Lists requests waiting for approval. |
| `/approve id:{number}` | Approves a request and sends it to the user's Sonarr or Radarr. The **Approve** button on a request card does the same. |
| `/deny id:{number} [reason]` | Denies a request. The reason is shown to the user. The **Deny** button asks for a reason. |
| `/requests [status] [type] [user]` | In the admin channel, lists everyone's requests, optionally for one user. |
| `/quota user:@someone` | In the admin channel, shows that user's usage. |
| `/request …` | In the admin channel, makes a request as yourself. Admin requests skip approval and quotas and use the default instance. |

### Media server access

| Command | What it does |
|---|---|
| `/mediaserver approve id:{number}` | Approves an access request: links the account. Plex invites follow in a later release. |
| `/mediaserver deny id:{number}` | Denies it. |

The **Approve** and **Deny** buttons on the access-request card do the same.

## Limits

Requestarr keeps within Discord's limits:

- Search results show at most 10 titles.
- Menus have at most 25 options. Very long shows offer All seasons, Future seasons, and the 23 most recent seasons.
- Labels are at most 100 characters.
- Messages are at most 2,000 characters.
