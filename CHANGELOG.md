# Changelog

Every pull request adds its changes here, under the version it ships in (see "How to add an entry" in
docs/development.md). On start, Requestarr posts each new version once: everything to the admin
channel, and only **For users** to the announcements channel, in the words written here.

## 0.2.0

### For users
- When you no longer need something you asked for, press **Remove…** on its message to put it to a vote.
- If a title you can watch might be removed, you get a "Still watching?" message: press **Keep it** to keep it. One keep is enough, nobody sees who pressed it, and you can take it back.
- If the admin wants to remove something you asked for, you're asked first. No answer by the date shown counts as yes.
- Everyone who was asked sees whether a title was kept or removed. A removed title can be asked for again any time.
- When someone switches a series between dub and sub, the message now says whether it's still on its way, partly there or ready, instead of always saying it's available.

### For admins
- Removal votes: **Delete** on a downloaded title's feed card, and **Remove a title** on the admin panel, which searches every Sonarr and Radarr library (titles nobody requested too). The requester is asked first; **Force removal** and **Drop removal** at every stage; removals show in `/admin requests`.
- Removing deletes the files and the title from Sonarr or Radarr without adding it to the exclusion list; removing seasons deletes their files and unmonitors them. The show stays.
- Settings `removal.voteDays`, `removal.requesterTimeoutDays` and `removal.reminderDay` (7, 7 and 5 days), stored where the admin web portal can change them.
- The TMDB logo and the feed card spacer are built into Requestarr and sent with each message; nothing is loaded from GitHub.

### Internal
- Every skipped test is accounted for: all are inherited platform-only tests. The test runner's printed summary leaves out tests skipped at fixture level, which is why it showed 24 where the result files show 76.

## 0.1.1

### Internal
- Live checks only use the test accounts named in `DISCORD_TEST_MEMBERS`, and can wait the real outage thresholds.

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
