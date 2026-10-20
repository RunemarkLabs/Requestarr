using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
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
            UseBackends();
            UseMetadata();
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());
            Mocker.SetConstant<IGuildSetupService>(Mocker.Resolve<GuildSetupService>());
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IPanelService>(Mocker.Resolve<PanelService>());
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());

            Mocker.Resolve<IGuildSetupService>().EnsureAsync().GetAwaiter().GetResult();

            Harness = new InteractionHarness(
                Mocker.Resolve<UserCommandHandler>(),
                Mocker.Resolve<MediaServerHandler>(),
                Mocker.Resolve<RequestHandler>(),
                Mocker.Resolve<ApprovalHandler>(),
                Mocker.Resolve<PanelHandler>(),
                Mocker.Resolve<HelpHandler>());
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

        // An /admin command as the server owner, in the admin channel ("user add" means /admin user add).
        protected FakeInteraction AdminCommand(string command, ulong member) =>
            FakeInteraction.SlashCommand("admin " + command).WithOption("member", member).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

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

        protected PermissionOverwrite OverwriteFor(FakeDiscordGuild.FakeChannel channel, ulong target) => channel.Overwrites.SingleOrDefault(o => o.TargetId == target);

        protected System.Collections.Generic.List<AuditEntry> AuditTrail => Mocker.Resolve<IAuditRepository>().All().OrderBy(a => a.Id).ToList();
    }
}
