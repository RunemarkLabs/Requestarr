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
                             "Either way, the final status says exactly which seasons were requested."
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
                Title = "Onboarding and access",
                Summary = "Add people and set what they may request",
                Intro = _ => "Adding someone creates their role, private channel and pinned panel. Access per type is off, needs approval, or auto-approve; `instance` sends one user's requests to a specific Sonarr or Radarr.",
                Commands = new[] { "admin user add", "admin user access", "admin user info", "admin user suspend", "admin user unsuspend", "admin user remove" },
                Buttons = new[] { PanelButtons.Users }
            },
            new HelpTopic
            {
                Key = "approvals",
                Title = "Approvals and the queue",
                Summary = "Approve or deny requests",
                Intro = _ => $"Requests that need approval post a card here with **{DiscordText.ApproveLabel}** and **{DiscordText.DenyLabel}**; the reason you give is shown to the user. Use **{DiscordText.AlreadyAvailableLabel}** when the title is already there; a request whose title is already in the library always ends as already available, never denied. When a request is approved, denied or cancelled, the user's request message and this card both change to show it. `/request` in this channel requests as you, skipping approval and limits.",
                Commands = new[] { "admin queue", "admin approve", "admin deny", "admin available", "admin cancel" },
                Buttons = new[] { PanelButtons.PendingQueue }
            },
            new HelpTopic
            {
                Key = "limits",
                Title = "Limits",
                Summary = "Requests per rolling window",
                Intro = _ => "A quota is per user and per type, over a rolling window (7 days unless you set `days`). `limit:0` removes it. Users see theirs under My limits.",
                Commands = new[] { "admin user quota" }
            },
            new HelpTopic
            {
                Key = "mediaserver",
                Title = "Media server requests",
                Summary = "Link Plex accounts",
                Intro = _ => "Users ask with Link Plex on their panel; the card here has Approve and Deny. You can also link an account directly.",
                Commands = new[] { "admin mediaserver approve", "admin mediaserver deny", "admin user mediaserver" }
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
