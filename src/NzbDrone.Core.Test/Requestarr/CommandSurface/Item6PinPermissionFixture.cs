using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 6: without Pin Messages the panels are posted unpinned, a health warning names the permission,
    // and no flow fails.
    [TestFixture]
    public class Item6PinPermissionFixture : RequestScenario
    {
        private FakeDiscordGuild.FakeChannel AliceRoom => ChannelFor(Alice);
        private FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        private HealthCheck Health()
        {
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.State).Returns(DiscordBotState.Connected);
            return Mocker.Resolve<DiscordHealthCheck>().Check();
        }

        private void LosePin()
        {
            Discord.CannotPin = true;
            AliceRoom.Pinned.Clear();
            AdminRoom.Pinned.Clear();
        }

        [Test]
        public void health_is_clean_when_the_bot_can_pin()
        {
            Health().Type.Should().Be(HealthCheckResult.Ok);
        }

        [Test]
        public void health_warns_and_names_pin_messages_when_missing()
        {
            Discord.CannotPin = true;

            var check = Health();

            check.Type.Should().Be(HealthCheckResult.Warning);
            check.Reason.Should().Be(HealthCheckReason.DiscordMissingPermission);
            check.Message.Should().Be(DiscordText.MissingPinMessages).And.Contain("Pin Messages");
        }

        [Test]
        public async Task panels_are_posted_unpinned_and_reused()
        {
            LosePin();
            var before = AliceRoom.Messages.Count;

            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
            await SetAccess(Alice, "tv", "off");

            AliceRoom.Pinned.Should().BeEmpty();
            AdminRoom.Pinned.Should().BeEmpty();
            AliceRoom.Messages.Count.Should().Be(before);
            AliceRoom.Messages.Single(m => m.Buttons.Any(b => b.CustomId == PanelRoutes.MyRequests)).Content.Should().NotContain("TV (");
            AdminRoom.Messages.Count(m => m.Buttons.Any(b => b.CustomId == PanelRoutes.AdminQueue)).Should().Be(1);
            UserFor(Alice).PanelMessageId.Should().NotBeNull();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task new_users_and_flows_never_fail_without_pin()
        {
            LosePin();

            (await Harness.SendAsync(AdminCommand("user add", Moderator))).LastReply.Content.Should().StartWith("Added");
            UserFor(Moderator).PanelMessageId.Should().NotBeNull();

            (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyLimits))).LastReply.Content.Should().Contain("Movies");
            (await RequestMovie(Alice, Dune)).LastReply.Content.Should().Contain(DiscordText.RequestPending("Dune (2021)", 1));
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task panels_get_pinned_once_the_permission_is_granted()
        {
            LosePin();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            Discord.CannotPin = false;
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            AliceRoom.Pinned.Should().ContainSingle().Which.ToString().Should().Be(UserFor(Alice).PanelMessageId);
            AdminRoom.Pinned.Should().ContainSingle();
            Health().Type.Should().Be(HealthCheckResult.Ok);
            ExceptionVerification.IgnoreWarns();
        }
    }
}
