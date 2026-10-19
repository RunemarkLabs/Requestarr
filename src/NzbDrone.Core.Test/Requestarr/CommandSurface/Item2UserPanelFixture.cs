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
    // Item 2: one pinned panel per user channel, kept current.
    [TestFixture]
    public class Item2UserPanelFixture : RequestScenario
    {
        private FakeDiscordGuild.FakeChannel Channel => ChannelFor(Alice);

        private string[] PanelButtons => Channel.PinnedMessage.Buttons.Select(b => b.Label).ToArray();

        [Test]
        public void new_user_channel_gets_one_pinned_panel()
        {
            Channel.Pinned.Should().ContainSingle();
            Channel.PinnedMessage.Content.Should().Contain("alice's requests").And.Contain("Movies (needs approval), TV (needs approval)");
            PanelButtons.Should().Equal(DiscordText.PanelMyRequests, DiscordText.PanelMyLimits, DiscordText.PanelLinkPlex);
            UserFor(Alice).PanelMessageId.Should().Be(Channel.Pinned.Single().ToString());
        }

        [Test]
        public async Task panel_updates_in_place_when_access_changes()
        {
            var before = Channel.Messages.Count;

            await SetAccess(Alice, "tv", "off");

            Channel.Messages.Count.Should().Be(before);
            Channel.PinnedMessage.Content.Should().Contain("Movies (needs approval)").And.NotContain("TV (");
        }

        [Test]
        public async Task link_plex_disappears_once_the_admin_links_an_account()
        {
            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "alice_plex"));

            PanelButtons.Should().Equal(DiscordText.PanelMyRequests, DiscordText.PanelMyLimits);
        }

        [Test]
        public async Task link_plex_opens_form_and_creates_access_request_then_disappears_on_approval()
        {
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
            Discord.DeleteMessage(Channel.Id, Channel.Pinned.Single());
            Channel.Pinned.Should().BeEmpty();

            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            Channel.Pinned.Should().ContainSingle();
            UserFor(Alice).PanelMessageId.Should().Be(Channel.Pinned.Single().ToString());
        }

        [Test]
        public async Task ensuring_panels_twice_does_not_duplicate()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            Channel.Pinned.Should().ContainSingle();
            Channel.Messages.Count(m => m.Buttons.Any(b => b.CustomId == PanelRoutes.MyRequests)).Should().Be(1);
        }

        [Test]
        public async Task panel_buttons_in_another_users_channel_are_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.MyRequests).By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
        }
    }
}
