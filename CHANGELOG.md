# Changelog

What changed in each version, for the people who use Requestarr. Every pull request adds its entry here (see
"Changelog" in docs/development.md). Two sections only, **For users** and **For admins**, in plain words; a section
with nothing in it is left out, and a version with nothing for either has only its heading. Requestarr posts these
in Discord: both sections in the admin channel, and **For users** in the announcements channel, word for word. Each
version heading carries the day it was made, which its card in Discord shows.

## 0.4.1 (2026-10-25)

### For users
- **Easier to read:** the welcome message, the blocked notice and Read changes are now coloured cards with large headings and text at normal message size, instead of small print.

### For admins
- **Your own request channel:** admins now ask for access from the welcome channel like everyone and get their own request channel straight away, with what they picked on automatic approval and nothing else.
- **The admin channel is for admin actions:** requesting there now points you to your own request channel instead of making the request.

## 0.4.0 (2026-10-24)

### For users
- **Asking for access:** friends who aren't members yet can now ask for access themselves from the new welcome channel, and they hear back privately.
- **Panels stay at the bottom:** your panel is always the newest message in your channel, so it's right where you look after a request message arrives.
- **What's new, short and tidy:** each update is now one line in the announcements channel with how many new things it brings and the main one; Read changes shows you the full list, just for you.
- **Outage notices:** while requests are paused, the notice stays pinned at the top of the announcements channel, and it goes away once things work again.
- **Where your channel lives:** your request channel and the announcements channel now sit together in the Requestarr Users group.
- **Link Plex is hidden for now:** the button comes back once the admin connects a Plex server.

### For admins
- **Two channel groups:** Requestarr keeps its channels in Requestarr Admin (the admin channel and the feed, admins only) and Requestarr Users (welcome, announcements and every member's channel); existing channels move over on the next start with their messages kept.
- **No second copies after setting up again:** Requestarr finds its own groups, channels and panels, and each member's channel, even when its saved data is gone, so a reinstall or a reset never doubles them, and a member who comes back gets their one channel back.
- **Welcome channel and access requests:** everyone in the server sees a welcome message explaining Requestarr with a Request access button; requests arrive as cards in the admin channel and the pending queue, where you approve with the access you choose or deny with a reason, and the person hears back privately.
- **Spam protection:** repeated clicks on the welcome buttons make people wait, and if they keep going a role blocks them and hides the welcome channel; you get one notice and can unblock from it or from the member card.
- **Panels stay at the bottom and aren't pinned:** every panel moves below anything new Requestarr posts in its channel, with no pings and no "pinned a message" notices.
- **Version cards:** each update is a one-line card counting the changes for admins and users, Read changes shows the full list privately, and the history so far is redone as cards once.
- **Outages:** the down alert (and the notice to users, if you announced it) is pinned while the outage lasts and removed when it's over, and every outage is kept in Requestarr's own history.
- **Media server buttons only when connected:** Link Plex and the welcome channel's media server buttons show only for a media server you've connected; none can be connected yet, so they're hidden for now.
- **Web app icons:** the web app shows Requestarr's logo instead of the old purple square, marked DEV, QA or BETA on the test servers, in light or dark to match the theme.

## 0.3.4 (2026-10-24)

## 0.3.3 (2026-10-24)

## 0.3.2 (2026-10-24)

## 0.3.1 (2026-10-24)

## 0.3.0 (2026-10-24)

### For users
- **What's new history:** the announcements channel keeps a post for every version, so you can scroll back through everything that has changed.

### For admins
- **What's new history:** the admin channel keeps a post for every version as a lasting record. A new install posts the full history once when it first starts, oldest first.
- **Clearer what's new posts:** each post shows only the sections that have something in them, and a version with nothing for you or your members posts nothing at all.
- **Published container image:** Requestarr is now published on GitHub's container registry for x86-64 and ARM servers, so you can pull it instead of building it yourself.

## 0.2.4 (2026-10-24)

## 0.2.3 (2026-10-24)

## 0.2.2 (2026-10-24)

## 0.2.1 (2026-10-24)

## 0.2.0 (2026-10-22)

### For users
- **Remove what you're done with:** press **Remove…** on a request's message to put the title to a vote.
- **Still watching?** If a title you can watch might be removed, you get a message first: press **Keep it** to keep it. One keep is enough, nobody sees who pressed it, and you can take it back.
- **Your say on the admin's removals:** if the admin wants to remove something you asked for, you're asked first, and no answer by the date shown counts as yes.
- **Vote results:** everyone who was asked sees whether a title was kept or removed, and a removed title can be asked for again any time.
- **Fixed:** switching a series between dub and sub always said it was available; the message now says whether it's still on its way, partly there or ready.

### For admins
- **Removal votes:** press **Delete** on a downloaded title's feed card, or **Remove a title** on the admin panel to search every Sonarr and Radarr library, titles nobody requested included. The requester is asked first, you can force or drop a removal at every stage, and removals show in /admin requests.
- **What a removal deletes:** the files and the title go from Sonarr or Radarr without being added to the exclusion list; removing seasons deletes their files and stops monitoring them, and the show stays.
- **Removal timing:** votes run 7 days, requesters have 7 days to answer and are reminded on day 5; all three can be changed in your provisioning settings, ready for the admin web portal.
- **Nothing loaded from outside:** the TMDB logo and the feed card spacer are built into Requestarr and sent with each message, instead of being loaded from GitHub.

## 0.1.1 (2026-10-21)

## 0.1.0 (2026-10-21)

### For users
- **Request movies and TV:** type /request, pick a type, type a title, and press **Request** on the one you want.
- **Seasons and anime:** choose which TV seasons you want, or all of them including new ones as they air; for anime, choose English dub or subtitles first.
- **Follow instead of asking twice:** if someone already asked for a title, press **Follow** to be told when it's ready, without using your limit.
- **Your request keeps you posted:** its message shows when it's waiting for approval, sent, downloading, stuck and ready to watch, and you can cancel it while it's still waiting for approval.
- **Your own channel:** a pinned panel with **My requests**, **My limits**, **Link Plex** and **Help**; link your Plex account there and the admin approves it.
- **Announcements:** a channel that tells you about new features and about outages that affect everyone.

### For admins
- **Discord set up for you:** on first start Requestarr creates the admin channel, the admin feed, the request channels' category, the members role and the announcements channel, and recreates any that are deleted.
- **Members:** add them from the admin panel's member picker or /admin user; each gets a role, a private channel and a pinned panel. Suspend, unsuspend and remove them; anyone who leaves the server is suspended automatically.
- **Access per member:** for each type, off, needs approval or auto-approve, a limit per rolling window, and which Sonarr or Radarr their requests go to.
- **Approvals in one place:** approval cards, the admin feed with one card per request, Plex link requests with Approve and Deny, and /admin requests to go through everything waiting on you one item at a time.
- **Download tracking:** Requestarr adds itself as a connection in Sonarr and Radarr to follow downloads, and checks regularly as a backup.
- **Anime:** sub and dub tags and release profiles on Sonarr, with optional separate sub and dub instances.
- **Connection alerts:** you're told when Sonarr or Radarr can't be reached and when it's back, with **Announce to users** after 30 minutes.
