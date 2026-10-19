using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    public abstract class RequestScenario : DiscordScenario
    {
        public const int Dune = 438631;
        public const int DuneTwo = 693134;
        public const int Arrival = 329865;
        public const int Severance = 371980;
        public const int LongShow = 100000;

        [SetUp]
        public async Task AddRequesters()
        {
            await AddUser(Alice);
            await AddUser(Bob);
        }

        protected IRequestService Requests => Mocker.Resolve<IRequestService>();

        protected IRequestRepository RequestStore => Mocker.Resolve<IRequestRepository>();

        protected BackendInstance Instance(string name) => Mocker.Resolve<IBackendService>().FindByName(name);

        protected Task<InteractionRun> SetAccess(ulong user, string type, string level, string instance = null)
        {
            var command = AdminCommand("user access", user).WithOption("type", type).WithOption("level", level);

            if (instance != null)
            {
                command.WithOption("instance", instance);
            }

            return Harness.SendAsync(command);
        }

        protected Task<InteractionRun> RequestMovie(ulong user, int tmdbId) =>
            Harness.SendAsync(FakeInteraction.Button($"{RequestRoutes.Make}:movie:{tmdbId}").By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)));

        protected Task<InteractionRun> RequestTv(ulong user, int tvdbId, params string[] picks) =>
            Harness.SendAsync(FakeInteraction.SelectMenu($"{RequestRoutes.Seasons}:{tvdbId}", picks).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)));

        protected Task<InteractionRun> AdminButton(string customId) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

        protected Task<InteractionRun> Admin(string command) =>
            Harness.SendAsync(FakeInteraction.SlashCommand("admin " + command).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

        protected FakeInteraction AdminSlash(string command) =>
            FakeInteraction.SlashCommand("admin " + command).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

        protected Request LastRequest => RequestStore.All().OrderBy(r => r.Id).Last();

        protected InteractionReply LastAdminMessage => Discord.Channels[AdminChannel].Messages.Last();

        protected string LastMessageIn(ulong user) => ChannelFor(user).Messages.Last().Content;
    }
}
