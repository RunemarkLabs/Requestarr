using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 5: #requestarr-admin has a pinned panel with Pending queue and Users.
    [TestFixture]
    public class Item5AdminPanelFixture : RequestScenario
    {
        [SetUp]
        public async Task Panels()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
        }

        private FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        [Test]
        public void admin_channel_has_one_pinned_panel_with_queue_and_users()
        {
            AdminRoom.Pinned.Should().ContainSingle();
            AdminRoom.PinnedMessage.Buttons.Select(b => (b.Label, b.CustomId)).Should().Equal(
                (DiscordText.PanelPendingQueue, PanelRoutes.AdminQueue),
                (DiscordText.PanelUsers, PanelRoutes.AdminUsers),
                (DiscordText.PanelMembers, "adm-pick"),
                (DiscordText.PanelHelp, PanelRoutes.AdminHelp));
            Config.AdminPanelMessageId.Should().Be(AdminRoom.Pinned.Single().ToString());
        }

        [Test]
        public async Task panel_is_not_duplicated_on_restart()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            AdminRoom.Pinned.Should().ContainSingle();
            AdminRoom.Messages.Count(m => m.Buttons.Any(b => b.CustomId == PanelRoutes.AdminQueue)).Should().Be(1);
        }

        [Test]
        public async Task pending_queue_button_starts_the_walk_and_shows_the_count()
        {
            (await AdminButton(PanelRoutes.AdminQueue)).LastReply.Content.Should().Be(DiscordText.WalkDone);

            await RequestMovie(Alice, Dune);
            var run = await AdminButton(PanelRoutes.AdminQueue);

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 1)).And.Contain("Dune (2021)").And.Contain("**alice** (asked)");
            PanelLabel(Requestarr.Core.Discord.PanelService.AdminPanel(1)).Should().Be("Pending queue (1)");
        }

        public static string PanelLabel(Requestarr.Core.Discord.Interactions.InteractionReply panel) => panel.Buttons.First(b => b.CustomId == PanelRoutes.AdminQueue).Label;

        [Test]
        public async Task users_button_lists_users_with_access()
        {
            var text = (await AdminButton(PanelRoutes.AdminUsers)).LastReply.Content;

            text.Should().Contain("• alice · active").And.Contain("• bob · active").And.Contain("Movies: needs approval");
        }

        [Test]
        public async Task admin_panel_buttons_are_admin_only_and_admin_channel_only()
        {
            (await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.AdminQueue).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel)))
                .LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.AdminUsers).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice))))
                .LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
        }
    }
}
