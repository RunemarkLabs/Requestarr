# Changelog

Every pull request adds its changes here, under the version it ships in (see "How to add an entry" in
docs/development.md). On start, Requestarr posts each new version once: everything to the admin
channel, and only **For users** to the announcements channel, in the words written here.

## 0.1.0

### For users
- Ask for movies and TV shows with `/request`: pick a type, type a title, and press **Request** on the one you want.
- Choose which TV seasons you want, or all seasons including new ones as they air.
- For anime, choose English dub or subtitles before picking seasons.
- If someone already asked for a title, press **Follow** to be told when it's ready, without using your limit.
- Your request message keeps you posted: waiting for approval, sent, downloading, stuck, and ready to watch.
- Your own channel has a pinned panel with **My requests**, **My limits**, **Link Plex** and **Help**.
- Link your Plex account from your panel; the admin approves it.
- Cancel a request while it's still waiting for approval.
- A new announcements channel tells you about new features and about outages that affect everyone.

### For admins
- Discord setup on first start: the admin channel, the admin feed, the request-channel category, the members role and the announcements channel, each recreated if deleted.
- Add members from the admin panel's member picker or `/admin user`; each gets a role, a private channel and a pinned panel.
- Per-member access per type (off, needs approval, auto-approve), limits per rolling window, and which Sonarr or Radarr their requests go to.
- Approval cards, the admin feed with one card per request, and `/admin requests` to go through everything waiting on you one item at a time.
- Suspend, unsuspend and remove members; members who leave the server are suspended automatically.
- Plex link requests with Approve and Deny.
- Download tracking through a webhook Requestarr adds to Sonarr and Radarr, with a backup poll.
- Anime sub and dub tags and release profiles on Sonarr, with optional separate sub and dub instances.
- Sonarr and Radarr connection states: alerts when one can't be reached and when it's back, with **Announce to users** after 30 minutes.
- What's new on each upgrade: the full list here, and the user-facing part in the announcements channel.

### Internal
- Servarr foundation from Prowlarr on .NET 10, SQLite, provisioning from `provisioning.yml`, Discord.Net over the gateway.
- Dev stack with test Sonarr and Radarr, GitHub build, secret scan and CodeQL checks.
- This changelog, checked on every pull request.
