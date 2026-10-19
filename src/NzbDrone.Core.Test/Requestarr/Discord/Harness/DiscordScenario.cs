using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.MediaServers;
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
            Mocker.SetConstant<IGuildSetupService>(Mocker.Resolve<GuildSetupService>());
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());

            Mocker.Resolve<IGuildSetupService>().EnsureAsync().GetAwaiter().GetResult();

            Harness = new InteractionHarness(Mocker.Resolve<UserCommandHandler>(), Mocker.Resolve<MediaServerHandler>());
        }

        protected IUserManagementService Users => Mocker.Resolve<IUserManagementService>();

        // An admin command as the server owner, in the admin channel.
        protected FakeInteraction AdminCommand(string command, ulong member) =>
            FakeInteraction.SlashCommand(command).WithOption("member", member).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

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
