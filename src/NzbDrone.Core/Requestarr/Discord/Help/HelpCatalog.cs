using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Help
{
    public enum HelpTree
    {
        User,
        Admin,

        // The admin's "What users see" branch: the user tree, unfiltered.
        AdminUser
    }

    // Who is reading: the content types they can request and whether Plex is linked.
    // Everything is the admin's view of user help and an admin who isn't a user.
    // Tmdb: a The Movie Database key is set, so its credits are shown.
    public record HelpViewer(IReadOnlyCollection<ContentType> Types, bool Linked, bool Tmdb = false)
    {
        public static HelpViewer Everything => new(ContentTypes.Requestable, false);

        public HelpViewer WithTmdb(bool on) => this with { Tmdb = on };
    }

    // A topic names the commands and buttons it explains; their usage and labels come from the
    // command definitions and the panel button registry, so help can't describe what's gone.
    public class HelpTopic
    {
        public string Key { get; init; }
        public string Title { get; init; }
        public string Summary { get; init; }
        public Func<HelpViewer, bool> Visible { get; init; } = _ => true;
        public Func<HelpViewer, string> Intro { get; init; }
        public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();
        public IReadOnlyList<PanelButton> Buttons { get; init; } = Array.Empty<PanelButton>();

        // The credits topic carries The Movie Database's logo.
        public bool ShowsTmdbLogo { get; init; }
    }

    public static class HelpCatalog
    {
        public const string RouteTopic = "help";
        public const string RouteHome = "help-home";
        public const string UsersBranchKey = "users";
        public const string UsersBranchTitle = "What users see";

        // Entry points of each tree; they count as covered for the help test.
        public static readonly IReadOnlyList<string> UserEntryCommands = new[] { "help" };
        public static readonly IReadOnlyList<string> AdminEntryCommands = new[] { "admin help" };
        public static readonly IReadOnlyList<PanelButton> UserEntryButtons = new[] { PanelButtons.Help };
        public static readonly IReadOnlyList<PanelButton> AdminEntryButtons = new[] { PanelButtons.AdminHelp };

        // The about / credits area The Movie Database's terms ask for; only while a key is set.
        public static readonly HelpTopic Credits = new()
        {
            Key = "credits",
            Title = "Credits",
            Summary = "Where cast data comes from",
            Visible = v => v.Tmdb,
            Intro = _ => DiscordText.TmdbNotice,
            ShowsTmdbLogo = true
        };

        public static readonly IReadOnlyList<HelpTopic> UserTopics = new[]
        {
            new HelpTopic
            {
                Key = "request",
                Title = "Requesting and searching by ID",
                Summary = "Find a title and request it",
                Visible = v => v.Types.Any(),
                Intro = v => $"Type `/request`, pick a type ({string.Join(", ", v.Types.Select(DiscordText.Label))}), then a title. " +
                             $"To search by ID, use {string.Join(" or ", v.Types.Select(t => t == ContentType.Tv ? "`tvdb:12345` for TV" : "`tmdb:603` for movies"))}.\n" +
                             "Each request stays in one message: results, details, then a clear final status.",
                Commands = new[] { "request" }
            },
            new HelpTopic
            {
                Key = "seasons",
                Title = "TV seasons",
                Summary = "All seasons or choose your own",
                Visible = v => v.Types.Contains(ContentType.Tv),
                Intro = _ => $"**{DiscordText.AllSeasons}**: requests every season now, and new seasons as they air, straight away. Specials aren't included.\n" +
                             $"**{DiscordText.ChooseSeasonsLabel}**: pick the seasons you want, and optionally \"{DiscordText.AlsoFutureSeasons}\". Specials are only included if you pick them. " +
                             $"Requestarr then says what you chose in plain words; press **{DiscordText.RequestLabel}** to send it or **{DiscordText.BackLabel}** to change it.\n" +
                             "Either way, the final status says exactly which seasons were requested.\n" +
                             $"Later, **{DiscordText.AddSeasonsLabel}** on the request's message adds more seasons (or new seasons as they air) at any time. No approval and no limit; it works on requests you follow too. Seasons are never removed there."
            },
            new HelpTopic
            {
                Key = "anime",
                Title = "Anime: sub or dub",
                Summary = "Pick dubbed or subtitled anime",
                Visible = v => v.Types.Contains(ContentType.Tv),
                Intro = _ => $"For anime, the card asks **{DiscordText.Label(Requests.AnimeLanguage.Dub)}** (English audio) or **{DiscordText.Label(Requests.AnimeLanguage.Sub)}** (original audio with subtitles) before seasons.\n" +
                             "A series is one language on the server. The card says which it's set to and the server's rule: dub wins, sub wins, or the first request decides. " +
                             "Asking for the language that wins switches the series (episodes already downloaded stay; new and missing ones come in the new language). Asking for the other one makes you a follower."
            },
            new HelpTopic
            {
                Key = "follow",
                Title = "Following an existing request",
                Summary = "Someone already asked for it",
                Visible = v => v.Types.Any(),
                Intro = _ => $"If someone already requested a title, its card shows **{DiscordText.FollowLabel}**. Following doesn't use your limit, and you're told when it's added."
            },
            new HelpTopic
            {
                Key = "downloads",
                Title = "Downloads and availability",
                Summary = "Downloading, stuck, ready to watch",
                Visible = v => v.Types.Any(),
                Intro = _ => $"Once a request is sent, its message keeps you posted: {DiscordText.FinalDownloading}, {DiscordText.FinalStalled} or {DiscordText.FinalDownloadFailed} (Sonarr or Radarr usually tries another release by itself), and {DiscordText.FinalReady} when it's ready to watch.\n" +
                             "For TV, a season counts as available when every episode that has aired is there, and the message says which seasons are ready. Each update replaces the message at the bottom of your channel and mentions you; people following the request get the same."
            },
            new HelpTopic
            {
                Key = "mine",
                Title = "My requests and cancelling",
                Summary = "See and cancel your requests",
                Intro = _ => $"A request that's waiting for approval has a **{DiscordText.CancelLabel}** button on its message.",
                Buttons = new[] { PanelButtons.MyRequests }
            },
            new HelpTopic
            {
                Key = "limits",
                Title = "Limits",
                Summary = "How many requests you have left",
                Intro = _ => "The admin can limit how many requests you make per type in a rolling window. Following someone else's request doesn't count.",
                Buttons = new[] { PanelButtons.MyLimits }
            },
            new HelpTopic
            {
                Key = "plex",
                Title = "Linking Plex",
                Summary = "Ask for access to the media server",
                Visible = v => !v.Linked,
                Intro = _ => "Enter your Plex username or email. The admin approves it, and the button disappears once your account is linked.",
                Buttons = new[] { PanelButtons.LinkPlex }
            },
            Credits
        };

        public static readonly IReadOnlyList<HelpTopic> AdminTopics = new[]
        {
            new HelpTopic
            {
                Key = "onboarding",
                Title = "Members: adding and managing",
                Summary = "Add people, set access, suspend, remove",
                Intro = _ => $"**{DiscordText.PanelMembers}** on the admin panel opens Discord's member picker; `/admin user` opens one member's card directly. The card shows only what applies now: **{DiscordText.AddMemberLabel}** for someone new (their role, private channel and panel; Movies and TV start as needs approval, Music off), " +
                             $"then one menu per content type showing its level (off, needs approval, auto-approve), a **{DiscordText.MorePlaceholder}** menu, and buttons to link Plex, **{DiscordText.SuspendLabel}** or **{DiscordText.UnsuspendLabel}** and **{DiscordText.RemoveLabel}** (it asks first). " +
                             "Users only see a content type once one of its Sonarr, Radarr or Lidarr instances has connected; you can grant it before that, and the card tells you. " +
                             $"**{DiscordText.PanelUsers}** lists everyone and opens the same card. Every change shows on the card at once.",
                Commands = new[] { "admin user" },
                Buttons = new[] { PanelButtons.Members, PanelButtons.Users }
            },
            new HelpTopic
            {
                Key = "approvals",
                Title = "Approvals and the queue",
                Summary = "Approve or deny requests",
                Intro = _ => $"Requests that need approval post a card here with **{DiscordText.ApproveLabel}** and **{DiscordText.DenyLabel}**; the reason you give is shown to the user. Use **{DiscordText.AlreadyAvailableLabel}** when the title is already there; a request whose title is already in the library always ends as already available, never denied. When a request is approved, denied or cancelled, the user's request message and this card both change to show it. " +
                             $"`/admin requests` (or **{DiscordText.PanelPendingQueue}** on the panel, which shows how many) goes through everything waiting on you, oldest first, one private card at a time: requests to approve, downloads that failed or got stuck, Plex link requests, and instances that are down. Each card has that item's actions plus **{DiscordText.SkipLabel}** and **{DiscordText.BackLabel}**; acting moves straight to the next. **{DiscordText.CancelLabel}** asks for an optional reason shown to the user. `/request` in this channel requests as you, skipping approval and limits.",
                Commands = new[] { "admin requests" },
                Buttons = new[] { PanelButtons.PendingQueue }
            },
            new HelpTopic
            {
                Key = "feed",
                Title = "The admin feed",
                Summary = "Every request, newest last",
                Intro = _ => $"#{ChannelLayout.FeedChannelName} has one small card per request: who asked, which instance, the state, and for TV the seasons. Every change moves its card to the bottom, so the newest activity is last. " +
                             $"Its buttons are only the ones that make sense now: **{DiscordText.ApproveLabel}**, **{DiscordText.DenyLabel}** and **{DiscordText.AlreadyAvailableLabel}** while it waits; **{DiscordText.CancelLabel}** while it waits or until something starts downloading (a sent title is then unmonitored in Sonarr or Radarr, never deleted); **{DiscordText.AddSeasonsLabel}** on TV. " +
                             "Approval cards stay in this channel too, and acting in either place updates both and the user's message. Account events (new users, access changes, suspensions, Plex links) stay in this channel, not the feed."
            },
            new HelpTopic
            {
                Key = "downloads",
                Title = "Downloads, webhooks and polling",
                Summary = "How Requestarr hears about downloads",
                Intro = _ => $"With `availability.webhookUrl` set (the address Sonarr and Radarr use to reach Requestarr), Requestarr adds its own connection, \"{Requestarr.Core.Backends.ArrApiBase.WebhookName}\", to each Sonarr and Radarr; it never changes your own connections. " +
                             "It also checks every few minutes (`availability.pollMinutes`, 15 by default) in case a call is missed, and `pollingOnly: true` on an instance skips the webhook. If an instance can't reach Requestarr, System → Status says so and the poll still works. " +
                             "A download counts as stuck when Sonarr or Radarr flag it (stalled, import blocked) or it doesn't move for 6 hours."
            },
            new HelpTopic
            {
                Key = "limits",
                Title = "Limits",
                Summary = "Requests per rolling window",
                Intro = _ => $"A limit is per member and per type, over a rolling window (7 days unless you set the days). Set it from the member card's **{DiscordText.MorePlaceholder}** menu; 0 removes it. Users see theirs under My limits.",
                Commands = new[] { "admin user" }
            },
            new HelpTopic
            {
                Key = "mediaserver",
                Title = "Media server requests",
                Summary = "Link Plex accounts",
                Intro = _ => $"Users ask with Link Plex on their panel; the card here has Approve and Deny, and the member's card shows the waiting request with **{DiscordText.ApproveLinkLabel}** and **{DiscordText.DenyLinkLabel}**. **{DiscordText.AdminLinkPlexLabel}** on a member card links an account directly.",
                Commands = new[] { "admin user" }
            },
            new HelpTopic
            {
                Key = "anime",
                Title = "Anime, sub/dub and priority",
                Summary = "How anime requests are set up in Sonarr",
                Intro = _ => $"On each Sonarr, Requestarr creates two tags (`{Anime.AnimeDefaults.DubTag}`, `{Anime.AnimeDefaults.SubTag}`) and two release profiles (\"{Anime.AnimeDefaults.DubProfile}\", \"{Anime.AnimeDefaults.SubProfile}\"). It never touches anything else. " +
                             "Anime is always added as Sonarr's Anime series type; you can switch a series to Standard in Sonarr.\n" +
                             "Each instance has a priority in provisioning (`anime.priority`: dub, sub or first) and adjustable release terms; `anime.enabled: false` turns sub/dub off there. " +
                             "Separate sub and dub instances work through per-user instances, but need different root folders (and ideally separate media server libraries), or the show appears twice."
            },
            new HelpTopic
            {
                Key = "visibility",
                Title = "The admin role and command visibility",
                Summary = "Who is an admin, who sees /admin",
                Intro = _ => $"Admins are the server owner and anyone with the **{DiscordSettings.DefaultAdminRoleName}** role (or the role set in provisioning). `/admin` only shows to members with Manage Server; to show it to the role, use Server Settings, Integrations, Requestarr. Requestarr re-checks the admin and the channel on every use either way."
            },
            Credits
        };

        public static IReadOnlyList<HelpTopic> Topics(HelpTree tree, HelpViewer viewer) =>
            (tree == HelpTree.Admin ? AdminTopics : UserTopics).Where(t => t.Visible(viewer)).ToList();

        public static InteractionReply Home(HelpTree tree, HelpViewer viewer)
        {
            var overview = tree switch
            {
                HelpTree.Admin => $"**Requestarr admin help**\nYou run Requestarr from this channel. Pick a topic, or **{UsersBranchTitle}** to walk someone through their help.",
                HelpTree.AdminUser => $"**{UsersBranchTitle}**\nThis is user help as a user with access to everything sees it. Each user only sees the topics they can use.",
                _ => viewer.Types.Any()
                    ? $"**Requestarr help**\nYou can request {string.Join(" and ", viewer.Types.Select(t => DiscordText.Label(t).ToLowerInvariant()))} here. Pick a topic."
                    : "**Requestarr help**\nYou can't request anything yet; ask the admin for access. Pick a topic."
            };

            return Page(tree, viewer, overview, home: true);
        }

        public static InteractionReply Topic(HelpTree tree, HelpViewer viewer, string key)
        {
            var topic = Topics(tree, viewer).FirstOrDefault(t => t.Key == key);

            return topic == null ? Home(tree, viewer) : Page(tree, viewer, $"**{topic.Title}**\n{Body(topic, viewer)}", home: false, topic);
        }

        public static string Body(HelpTopic topic, HelpViewer viewer)
        {
            var lines = new List<string> { topic.Intro(viewer) };
            var leaves = DiscordCommandDefinitions.Leaves().ToDictionary(l => l.Path);

            foreach (var path in topic.Commands)
            {
                var leaf = leaves[path];
                lines.Add($"• `{leaf.Usage}`: {leaf.Description}");
            }

            foreach (var button in topic.Buttons)
            {
                lines.Add($"• **{button.Label}** on {(PanelButtons.Admin.Contains(button) ? "the admin panel" : "your pinned panel")}");
            }

            return DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength - 100);
        }

        private static InteractionReply Page(HelpTree tree, HelpViewer viewer, string content, bool home, HelpTopic topic = null)
        {
            var options = Topics(tree, viewer).Select(t => new InteractionSelectOption(t.Title, t.Key, t.Summary)).ToList();

            if (tree == HelpTree.Admin)
            {
                options.Add(new InteractionSelectOption(UsersBranchTitle, UsersBranchKey, "Walk a user through their help"));
            }

            var buttons = new List<InteractionButton>();

            if (!home || tree == HelpTree.AdminUser)
            {
                var backTo = home ? HelpTree.Admin : tree;
                buttons.Add(new InteractionButton { CustomId = $"{RouteHome}:{Key(backTo)}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary });
            }

            return new InteractionReply
            {
                Content = content,
                Ephemeral = true,
                Embeds = topic?.ShowsTmdbLogo == true
                    ? new List<InteractionEmbed> { new() { Title = DiscordText.TmdbName, Url = DiscordText.TmdbSite, ThumbnailUrl = DiscordText.TmdbLogoUrl } }
                    : new List<InteractionEmbed>(),
                SelectMenu = options.Any() ? new InteractionSelectMenu { CustomId = $"{RouteTopic}:{Key(tree)}", Placeholder = "Pick a topic", Options = options } : null,
                Buttons = buttons
            };
        }

        public static string Key(HelpTree tree) => tree switch
        {
            HelpTree.Admin => "admin",
            HelpTree.AdminUser => "adminuser",
            _ => "user"
        };

        public static HelpTree? ParseKey(string key) => key switch
        {
            "admin" => HelpTree.Admin,
            "adminuser" => HelpTree.AdminUser,
            "user" => HelpTree.User,
            _ => null
        };
    }
}
