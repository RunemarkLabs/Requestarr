using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Metadata;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Discord.Harness
{
    // Real services, real test database, fake Discord server, driven through the real
    // dispatcher by simulated interactions.
    public abstract class DiscordScenario : DbTest
    {
        public const ulong AdminRole = 900000000000000900;
        public const ulong Alice = 900000000000001001;
        public const ulong Bob = 900000000000001002;
        public const ulong Moderator = 900000000000001003;

        protected FakeDiscordGuild Discord { get; private set; }
        protected FakeRadarr Radarr { get; private set; }
        protected FakeSonarr Sonarr { get; private set; }
        protected FakeTmdb Tmdb { get; set; }
        protected TmdbSettingsStore TmdbKey { get; set; }
        protected InteractionHarness Harness { get; private set; }

        // Media servers the admin has connected (none by default, as on a real server today).
        protected FakeMediaServers ConnectedMediaServers { get; private set; }
        protected DiscordGuildConfig Config => Mocker.Resolve<IGuildSetupService>().GetConfig();
        protected ulong AdminChannel => ulong.Parse(Config.AdminChannelId);

        [SetUp]
        public void SetUpScenario()
        {
            Discord = new FakeDiscordGuild();
            Discord.AddMember(Alice, "alice");
            Discord.AddMember(Bob, "bob");
            Discord.AddMember(Moderator, "mod", false, AdminRole);
            Discord.Roles[AdminRole] = "Requestarr Admin";

            var settings = new DiscordSettingsStore();
            settings.Set(new DiscordSettings { BotToken = "test", ApplicationId = 1, GuildId = FakeDiscordGuild.Guild, AdminRoleId = AdminRole });

            Mocker.SetConstant<IDiscordSettingsStore>(settings);
            Mocker.SetConstant<IDiscordGuildGateway>(Discord);
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IUserMediaAccessRepository>(Mocker.Resolve<UserMediaAccessRepository>());
            Mocker.SetConstant<IMediaServerAccountRepository>(Mocker.Resolve<MediaServerAccountRepository>());
            Mocker.SetConstant<IMediaServerAccessRequestRepository>(Mocker.Resolve<MediaServerAccessRequestRepository>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IDiscordGuildConfigRepository>(Mocker.Resolve<DiscordGuildConfigRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IBackendRepository>(Mocker.Resolve<BackendRepository>());
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalProposalRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalProposalRepository>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalFlagRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalFlagRepository>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalPromptRepository>(Mocker.Resolve<Requestarr.Core.Removals.RemovalPromptRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigRepository>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigRepository>());
            Mocker.SetConstant<NzbDrone.Core.Configuration.IConfigService>(Mocker.Resolve<NzbDrone.Core.Configuration.ConfigService>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalSettings>(Mocker.Resolve<Requestarr.Core.Removals.RemovalSettings>());
            Mocker.SetConstant<Requestarr.Core.Access.IAccessRequestRepository>(Mocker.Resolve<Requestarr.Core.Access.AccessRequestRepository>());
            Mocker.SetConstant<Requestarr.Core.Access.IWelcomeGuardRepository>(Mocker.Resolve<Requestarr.Core.Access.WelcomeGuardRepository>());
            Mocker.SetConstant<Requestarr.Core.Access.IAccessSettings>(Mocker.Resolve<Requestarr.Core.Access.AccessSettings>());
            Mocker.SetConstant<IOutageRepository>(Mocker.Resolve<OutageRepository>());
            ConnectedMediaServers = new FakeMediaServers();
            Mocker.SetConstant<IMediaServerConnections>(ConnectedMediaServers);
            UseBackends();

            // As on startup: instances that answer count as connected (users see their content types).
            Mocker.Resolve<IBackendService>().CheckAllAsync().GetAwaiter().GetResult();
            UseMetadata();
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());
            Mocker.SetConstant<IGuildSetupService>(Mocker.Resolve<GuildSetupService>());
            Mocker.SetConstant<Requestarr.Core.Admin.IAdminQueue>(new Requestarr.Core.Admin.AdminQueue(new Requestarr.Core.Admin.IAdminQueueSource[]
            {
                Mocker.Resolve<Requestarr.Core.Admin.PendingRequestsSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.TroubleSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.PlexLinkSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.DownInstanceSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.RemovalSource>(),
                Mocker.Resolve<Requestarr.Core.Admin.AccessRequestSource>()
            }));
            Mocker.SetConstant<Requestarr.Core.Announcements.IAnnouncementService>(Mocker.Resolve<Requestarr.Core.Announcements.AnnouncementService>());
            Mocker.SetConstant<IConnectionMonitor>(Mocker.Resolve<ConnectionMonitor>());
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IPanelService>(Mocker.Resolve<PanelService>());
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());
            Mocker.SetConstant<Requestarr.Core.Access.IAccessRequestService>(Mocker.Resolve<Requestarr.Core.Access.AccessRequestService>());
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestFeed>(Mocker.Resolve<RequestFeed>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            Mocker.SetConstant<Requestarr.Core.Removals.IRemovalService>(Mocker.Resolve<Requestarr.Core.Removals.RemovalService>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());

            Mocker.Resolve<IGuildSetupService>().EnsureAsync().GetAwaiter().GetResult();

            Harness = new InteractionHarness(
                Mocker.Resolve<AdminConsoleHandler>(),
                Mocker.Resolve<MediaServerHandler>(),
                Mocker.Resolve<RequestHandler>(),
                Mocker.Resolve<ApprovalHandler>(),
                Mocker.Resolve<PanelHandler>(),
                Mocker.Resolve<HelpHandler>(),
                Mocker.Resolve<SeasonsHandler>(),
                Mocker.Resolve<AdminRequestsHandler>(),
                Mocker.Resolve<OutageHandler>(),
                Mocker.Resolve<RemovalHandler>(),
                Mocker.Resolve<WelcomeHandler>(),
                Mocker.Resolve<AccessRequestHandler>(),
                Mocker.Resolve<VersionCardHandler>());
        }

        // Fake The Movie Database (no key) by default; live fixtures may use the real client.
        protected virtual void UseMetadata()
        {
            Tmdb = new FakeTmdb();
            TmdbKey = new TmdbSettingsStore();
            Mocker.SetConstant<ITmdbSettingsStore>(TmdbKey);
            Mocker.SetConstant<ITmdbClient>(Tmdb);
        }

        // Fake Radarr / Sonarr by default; live fixtures override with the real APIs.
        protected virtual void UseBackends()
        {
            Radarr = new FakeRadarr();
            Sonarr = new FakeSonarr();
            Mocker.SetConstant<IRadarrApi>(Radarr);
            Mocker.SetConstant<ISonarrApi>(Sonarr);
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Radarr", Url = "http://radarr-test:7878", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", Default = true },
                    new() { Name = "Radarr 4K", Url = "http://radarr-4k:7878", ApiKey = "k", QualityProfile = "Any", RootFolder = "/data/media" }
                },
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Sonarr", Url = "http://sonarr-test:8989", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", SeriesType = "anime" }
                }
            });
        }

        protected IUserManagementService Users => Mocker.Resolve<IUserManagementService>();

        // One of the old /admin commands ("user add", "user access", ...) for a member, done the way it
        // is done now (see OldAdminCommand). As the server owner, in the admin channel.
        protected OldAdminCommand AdminCommand(string command, ulong member) => OldAdmin(command).WithOption("member", member);

        protected OldAdminCommand OldAdmin(string command) => new(command, RunOldAdminAsync);

        // An interaction by the server owner in the admin channel.
        protected FakeInteraction AsAdmin(FakeInteraction interaction) => interaction.By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

        // /admin user {member}: the member's card.
        protected Task<InteractionRun> MemberCard(ulong member) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin user").WithOption("member", member)));

        // A button on a member card ("adm-suspend" means adm-suspend:{member}).
        protected Task<InteractionRun> MemberButton(ulong member, string route, string extra = null) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{route}:{member}{(extra != null ? ":" + extra : string.Empty)}")));

        // A choice from one of the member card's menus.
        protected Task<InteractionRun> MemberMenu(ulong member, string route, string value) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu($"{route}:{member}", value)));

        // A form opened from a member card, submitted.
        protected Task<InteractionRun> MemberForm(string customId, params (string Field, string Value)[] fields)
        {
            var form = FakeInteraction.ModalSubmit(customId);

            foreach (var (field, value) in fields)
            {
                form.WithField(field, value);
            }

            return Harness.SendAsync(AsAdmin(form));
        }

        // A button on a request card (/admin request), and the deny / cancel forms it opens.
        protected Task<InteractionRun> RequestCardButton(string customId) => Harness.SendAsync(AsAdmin(FakeInteraction.Button(customId)));

        protected Task<InteractionRun> RequestCardForm(string customId, string reason) =>
            Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(customId).WithField(RequestRoutes.ReasonField, reason ?? string.Empty)));

        private async Task<InteractionRun> RunOldAdminAsync(OldAdminCommand old)
        {
            var member = old.Has("member") ? Convert.ToUInt64(old.Options["member"]) : 0UL;
            var id = old.Get<int>("id");

            switch (old.Command)
            {
                case "user add":
                {
                    var add = await MemberButton(member, AdminRoutes.Add);

                    foreach (var type in new[] { "movie", "tv", "music" })
                    {
                        if (old.Has(type) && UserFor(member) != null)
                        {
                            await MemberMenu(member, AdminRoutes.Access, $"{type}:{old.Get<string>(type)}");
                        }
                    }

                    return add;
                }

                case "user remove":
                    await MemberButton(member, AdminRoutes.Remove);
                    return await MemberButton(member, AdminRoutes.RemoveConfirm);

                case "user suspend":
                    return await MemberButton(member, AdminRoutes.Suspend);

                case "user unsuspend":
                    return await MemberButton(member, AdminRoutes.Unsuspend);

                case "user info":
                    return await MemberCard(member);

                case "user access":
                {
                    var type = old.Get<string>("type");
                    var run = await MemberMenu(member, AdminRoutes.Access, $"{type}:{old.Get<string>("level")}");

                    if (old.Has("instance"))
                    {
                        var instance = Mocker.Resolve<IBackendService>().FindByName(old.Get<string>("instance"));
                        run = await MemberMenu(member, AdminRoutes.Instance, $"{type}:{instance?.Id.ToString() ?? old.Get<string>("instance")}");
                    }

                    return run;
                }

                case "user quota":
                {
                    var type = old.Get<string>("type");
                    await MemberMenu(member, AdminRoutes.Limit, type);
                    var fields = new List<(string, string)> { (AdminRoutes.LimitField, old.Get<long>("limit").ToString()) };

                    if (old.Has("days"))
                    {
                        fields.Add((AdminRoutes.DaysField, old.Get<long>("days").ToString()));
                    }

                    return await MemberForm($"{AdminRoutes.LimitForm}:{member}:{type}", fields.ToArray());
                }

                case "user mediaserver":
                    await MemberButton(member, AdminRoutes.Plex);
                    return await MemberForm($"{AdminRoutes.PlexForm}:{member}", (AdminRoutes.PlexField, old.Get<string>("username")));

                // Task 3c: the queue is /admin requests (one item at a time); a member's requests are on
                // their card's "More…" menu. There's no filter by type any more.
                case "queue" when old.Has("user"):
                    return await MemberMenu(Convert.ToUInt64(old.Options["user"]), AdminRoutes.More, "requests");

                case "queue":
                    return await Harness.SendAsync(AsAdmin(FakeInteraction.Button(PanelRoutes.AdminQueue)));

                case "approve":
                    return await RequestCardButton($"{RequestRoutes.Approve}:{id}");

                case "available":
                    return await RequestCardButton($"{RequestRoutes.Available}:{id}");

                case "deny":
                {
                    var click = await RequestCardButton($"{RequestRoutes.Deny}:{id}");
                    return click.Responder.LastModal == null ? click : await RequestCardForm($"{RequestRoutes.DenyReason}:{id}", old.Get<string>("reason"));
                }

                case "cancel":
                {
                    var click = await RequestCardButton($"{RequestRoutes.AdminCancel}:{id}");
                    return click.Responder.LastModal == null ? click : await RequestCardForm($"{RequestRoutes.CancelReason}:{id}", old.Get<string>("reason"));
                }

                case "mediaserver approve":
                case "mediaserver deny":
                {
                    var request = Mocker.Resolve<IMediaServerAccessRequestRepository>().Find(id);
                    var discordId = request == null ? 0UL : ulong.Parse(Mocker.Resolve<IUserIdentityRepository>().GetForUser(request.UserId).First(i => i.Provider == UserIdentity.DiscordProvider).ExternalId);
                    return await MemberButton(discordId, AdminRoutes.Link, $"{id}:{(old.Command.EndsWith("approve") ? "a" : "d")}");
                }

                case "help":
                    return await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin help")));
            }

            throw new ArgumentException($"no such old admin command: {old.Command}");
        }

        // /request {type} {query} by a user in their own channel.
        protected FakeInteraction Search(ulong userId, string type, string query) =>
            FakeInteraction.SlashCommand("request").WithOption("type", type).WithOption("query", query).By(userId).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(userId));

        // /request from the admin channel by the server owner.
        protected FakeInteraction AdminRequest(string type, string query) =>
            FakeInteraction.SlashCommand("request").WithOption("type", type).WithOption("query", query).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

        // The Link Plex form from the user's panel, submitted.
        protected FakeInteraction PlexForm(ulong userId, string username) =>
            FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, username).By(userId).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(userId));

        protected FakeInteraction PanelButton(ulong userId, string route) =>
            FakeInteraction.Button(route).By(userId).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(userId));

        protected FakeInteraction CancelButton(ulong userId, int requestId) =>
            FakeInteraction.Button($"req-cancel:{requestId}").By(userId).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(userId));

        protected FakeInteraction UserCommand(string command, ulong userId) =>
            FakeInteraction.SlashCommand(command).By(userId).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(userId));

        protected async Task<InteractionRun> AddUser(ulong member, string movie = null, string tv = null, string music = null)
        {
            var command = AdminCommand("user add", member);

            if (movie != null)
            {
                command.WithOption("movie", movie);
            }

            if (tv != null)
            {
                command.WithOption("tv", tv);
            }

            if (music != null)
            {
                command.WithOption("music", music);
            }

            return await Harness.SendAsync(command);
        }

        protected RequestarrUser UserFor(ulong discordId) => Users.FindByDiscordId(discordId);

        protected ulong ChannelOf(ulong discordId) => ulong.Parse(UserFor(discordId).PrivateChannelId);

        protected ulong RoleOf(ulong discordId) => ulong.Parse(UserFor(discordId).DiscordRoleId);

        protected FakeDiscordGuild.FakeChannel ChannelFor(ulong discordId) => Discord.Channels[ChannelOf(discordId)];

        // The panel Requestarr keeps in a channel, found by the ID it stored (panels aren't pinned since task 4e).
        protected ulong? PanelIdIn(FakeDiscordGuild.FakeChannel channel)
        {
            var config = Config;
            var id = channel.Id.ToString();
            var stored = id switch
            {
                _ when id == config.AdminChannelId => config.AdminPanelMessageId,
                _ when id == config.WelcomeChannelId => config.WelcomeMessageId,
                _ when id == config.BlockedChannelId => config.BlockedMessageId,
                _ => Mocker.Resolve<IRequestarrUserRepository>().FindByChannel(id)?.PanelMessageId
            };

            return ulong.TryParse(stored, out var panel) && channel.MessageIds.Contains(panel) ? panel : null;
        }

        protected Requestarr.Core.Discord.Interactions.InteractionReply PanelIn(FakeDiscordGuild.FakeChannel channel) =>
            PanelIdIn(channel) is { } id ? channel.Messages[channel.MessageIds.IndexOf(id)] : null;

        // Every message in a channel that is a panel of this kind (counted to show there's only one).
        protected int PanelsIn(FakeDiscordGuild.FakeChannel channel, string route) => channel.Messages.Count(m => m.Buttons.Concat(m.Cards.SelectMany(c => c.Buttons)).Any(b => b.CustomId == route));

        protected PermissionOverwrite OverwriteFor(FakeDiscordGuild.FakeChannel channel, ulong target) => channel.Overwrites.SingleOrDefault(o => o.TargetId == target);

        protected System.Collections.Generic.List<AuditEntry> AuditTrail => Mocker.Resolve<IAuditRepository>().All().OrderBy(a => a.Id).ToList();
    }
}
