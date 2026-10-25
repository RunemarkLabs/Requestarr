using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 2: one panel per user channel, kept current. Since task 4e panels aren't pinned (they're always the newest
    // message instead), and Link Plex shows only while a Plex server is connected.
    [TestFixture]
    public class Item2UserPanelFixture : RequestScenario
    {
        private FakeDiscordGuild.FakeChannel Channel => ChannelFor(Alice);

        private string[] PanelButtons => PanelIn(Channel).Buttons.Select(b => b.Label).ToArray();

        private async Task ConnectPlex()
        {
            ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            await Mocker.Resolve<IPanelService>().RefreshUserPanelAsync(UserFor(Alice));
        }

        [Test]
        public void new_user_channel_gets_one_panel_and_no_link_plex_without_a_plex_server()
        {
            Channel.Pinned.Should().BeEmpty("panels are always the newest message, so they aren't pinned");
            PanelsIn(Channel, PanelRoutes.MyRequests).Should().Be(1);
            PanelIn(Channel).Content.Should().Contain("Requestarr · alice").And.Contain($"Welcome <@{Alice}>").And.Contain("Movies (needs approval), TV (needs approval)");
            PanelButtons.Should().Equal(DiscordText.PanelMyRequests, DiscordText.PanelMyLimits, DiscordText.PanelHelp);
            UserFor(Alice).PanelMessageId.Should().Be(Channel.MessageIds.Last().ToString());
        }

        [Test]
        public async Task link_plex_shows_once_a_plex_server_is_connected()
        {
            await ConnectPlex();

            PanelButtons.Should().Equal(DiscordText.PanelMyRequests, DiscordText.PanelMyLimits, DiscordText.PanelLinkPlex, DiscordText.PanelHelp);
        }

        [Test]
        public async Task panel_updates_in_place_when_access_changes()
        {
            var before = Channel.Messages.Count;

            await SetAccess(Alice, "tv", "off");

            Channel.Messages.Count.Should().Be(before);
            PanelIn(Channel).Content.Should().Contain("Movies (needs approval)").And.NotContain("TV (");
        }

        [Test]
        public async Task link_plex_disappears_once_the_admin_links_an_account()
        {
            await ConnectPlex();
            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "alice_plex"));

            PanelButtons.Should().Equal(DiscordText.PanelMyRequests, DiscordText.PanelMyLimits, DiscordText.PanelHelp);
        }

        [Test]
        public async Task link_plex_opens_form_and_creates_access_request_then_disappears_on_approval()
        {
            await ConnectPlex();
            var open = await Harness.SendAsync(PanelButton(Alice, PanelRoutes.LinkPlex));
            open.Responder.LastModal.CustomId.Should().Be(PanelRoutes.LinkPlexForm);

            var submit = await Harness.SendAsync(PlexForm(Alice, "alice_plex"));
            submit.LastReply.Content.Should().Be(DiscordText.AccessRequestSent(MediaServerProvider.Plex));
            Discord.Channels[AdminChannel].Messages.Last().Buttons.Select(b => b.CustomId).Should().Equal("msreq-approve:1", "msreq-deny:1");
            PanelButtons.Should().Contain(DiscordText.PanelLinkPlex);

            await AdminButton("msreq-approve:1");

            PanelButtons.Should().NotContain(DiscordText.PanelLinkPlex);
        }

        [Test]
        public async Task my_requests_and_my_limits_answer_privately()
        {
            await RequestMovie(Alice, Dune);

            var requests = (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyRequests))).LastReply;
            requests.Ephemeral.Should().BeTrue();
            requests.Content.Should().Contain("#1 ·").And.Contain("Dune (2021)");

            var limits = (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyLimits))).LastReply;
            limits.Ephemeral.Should().BeTrue();
            limits.Content.Should().Contain("Movies: needs approval, no limit");
        }

        [Test]
        public async Task existing_channels_get_a_panel_on_next_start_and_deleted_panels_come_back()
        {
            Discord.DeleteMessage(Channel.Id, PanelIdIn(Channel).Value);
            PanelIn(Channel).Should().BeNull();

            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            PanelsIn(Channel, PanelRoutes.MyRequests).Should().Be(1);
            UserFor(Alice).PanelMessageId.Should().Be(Channel.MessageIds.Last().ToString());
        }

        [Test]
        public async Task ensuring_panels_twice_does_not_duplicate()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            Channel.Pinned.Should().BeEmpty();
            PanelsIn(Channel, PanelRoutes.MyRequests).Should().Be(1);
        }

        [Test]
        public async Task panel_buttons_in_another_users_channel_are_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.MyRequests).By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
        }
    }
}
