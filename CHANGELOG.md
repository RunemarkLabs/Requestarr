# Changelog

What changed, for the people who use Requestarr. Two sections only, **For users** and **For admins**, in plain words;
a section with nothing in it is left out, and a version with nothing for either has only its heading. Requestarr posts
these in Discord: both sections in the admin channel, and **For users** in the announcements channel, word for word.
Each version heading carries the day it was made, which its card in Discord shows.

Unreleased is what changed since the last build the beta testers got, written as where things ended up: a change to
something already under Unreleased rewrites that line, and a change to something the testers already have is a new
line (see "Changelog" in docs/development.md). The staging promotion gives it the build's number.

Numbering changed with build 0.7.0004. Up to 0.3.4 every change took its own number, and those stay as they are; the
versions after it, 0.4.0 to 0.6.0, never reached the testers and are folded into Unreleased. From 0.7.0004 on, a number
is a build sent to the beta testers, counting every build, and the release is that same build.

## Unreleased

### For users
- **Asking for access:** friends who aren't members yet can ask for access themselves from the welcome channel, and they hear back privately.
- **Access requests paused:** pressing the request button too many times in a short while pauses access requests from that person; an admin can lift the pause, and the message they see says so.
- **Your channel:** your request channel and the announcements channel sit together in the Requestarr Users group, and your panel is always the newest message in your channel, right where you look after a request message arrives.
- **Update cards:** each update in the announcements channel shows its build number, like 0.7.0004, and the day it came out, with the main change underneath and how many more; Read changes shows you the full list, just for you.
- **Easier to read:** the welcome message, the paused notice and Read changes are coloured cards with large headings and text at normal message size.
- **No stacked copies:** pressing a button that answers only you, like Help, My requests or Read changes, again replaces your earlier answer instead of adding another copy under it.
- **Outage notices:** while requests are paused, the notice stays pinned at the top of the announcements channel, and it goes away once things work again.
- **Report a problem:** the bug reports channel has a Report a problem button: pick the part of Requestarr, say what happened and what you expected, and it reaches the people who build Requestarr. Reports are public, and the form says so before you send. Reports stay in the order they were sent with the button below them, and your report's card is the confirmation.
- **Softer logo:** the arrow in Requestarr's logo is a soft charcoal instead of solid black, so it sits more gently next to the teal.
- **Link Plex is hidden for now:** the button comes back once the admin connects a Plex server.

### For admins
- **Build numbers:** every build sent to the beta testers has one number that counts up, like 0.7.0004, shown the same on the update cards, in the web app and on the logo, and a release is that exact build, never rebuilt, so production runs what the testers ran. Going back to an earlier build means restoring the backup the beta update takes every time, which its rollback puts back.
- **Two channel groups:** Requestarr keeps its channels in Requestarr Admin (the admin channel and the feed, admins only) and Requestarr Users (welcome, announcements, bug reports and every member's channel); existing channels move over on the next start with their messages kept.
- **No second copies after setting up again:** Requestarr finds its own groups, channels and panels, and each member's channel, even when its saved data is gone, so a reinstall or a reset never doubles them, and a member who comes back gets their one channel back.
- **No stacked copies for admins:** pressing Pending queue, Users, Help or Read changes again replaces your earlier private answer instead of stacking copies.
- **Access requests:** everyone in the server sees a welcome message with a Request access button; requests arrive as cards in the admin channel and the pending queue, where you approve with the access you choose or deny with a reason, and the person hears back privately. Admins ask from the welcome channel too and get their own request channel straight away; the admin channel is for admin actions only.
- **Spam protection:** repeated clicks on the welcome buttons make people wait, and if they keep going their access requests are paused: a role hides the welcome channel and shows them a message that an admin can lift the pause. You get one notice; Unblock on it or on their member card lifts it, and so does removing the role.
- **Panels stay at the bottom:** every panel moves below anything new Requestarr posts in its channel, with no pings, no "pinned a message" notices, and no "edited" mark each time it moves or Requestarr restarts.
- **Update cards in the admin channel:** each update is a card with its build number and day, the main change counting both sections and how many more; Read changes shows the full list privately. The update history already in your channels is redone as cards once, on the first start of this build.
- **Outages:** the down alert, and the notice to users if you announced it, is pinned while the outage lasts and removed when it's over, and every outage is kept in Requestarr's own history.
- **Media server buttons only when connected:** Link Plex and the welcome channel's media server buttons show only for a media server you've connected; none can be connected yet, so they're hidden for now.
- **The web app's logo:** Requestarr's logo replaces the old purple square everywhere, including while the app loads. It's full height with a colored bar naming the environment (Dev, QA or Beta; white on the light theme's purple header), sits straight on the header with no box around it in both themes, and uses the light arrow on the sign-in page. On your live install the bar shows the version you run, which also shows when you point at the logo.
- **Icons:** the browser tab, home screen icons and tiles use the charcoal arrow, on charcoal gray in the dark theme instead of black.
- **The bot keeps its own picture and banner:** Requestarr gives its bot the Requestarr picture and your environment's banner and updates them when a release changes them, so you no longer upload them by hand. A picture you chose yourself is kept, and you can turn this off in the provisioning file.
- **Bug reports become issues:** reports can be filed on GitHub with the version, the part of Requestarr and who reported it, in the Requestarr organization; it's off unless you turn it on with your own access key, and each person can only send a few in a row. The dev, QA and beta servers always file; a report that couldn't be filed says exactly why on its card and is filed on its own once it can be. If your setup file names the old home, Runemark Labs, change it and give the server a new access key made for the Requestarr organization.
- **New home on GitHub:** Requestarr has its own GitHub organization, also called Requestarr: the links in the web app's Status page lead there and beta updates come from there, at the image address the beta setup guide shows.
- **Kept up to date:** the parts Requestarr is built from are updated every week, and a build that fixes a security problem in something Requestarr ships says so here.
- **Safer logs:** a web address or user name with a line break in it can no longer add fake lines to Requestarr's log.
- **The QA checklist (test servers):** on the QA server each build comes with a list of things to test in Discord, in an admin-only Requestarr QA group. Admins mark each as passing or failing (several in a row if needed); a failing one opens a linked bug report, and passes carry over to the next build as long as an item's wording doesn't change.

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
