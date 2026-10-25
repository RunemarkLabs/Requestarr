using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 3: Link Plex on member panels, and its help topic, show only while a Plex server is connected. None
    // can be connected yet, so both are gone for now; the admin help says so.
    [TestFixture]
    public class Item3LinkPlexFixture : WelcomeScenario
    {
        private async Task<string[]> HelpTopics(ulong member)
        {
            var run = await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.Help).By(member).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(member)));
            return run.LastReply.SelectMenu.Options.Select(o => o.Value).ToArray();
        }

        [Test]
        public async Task without_a_plex_server_there_is_no_link_plex_button_or_topic()
        {
            await AddUser(Alice);

            PanelIn(ChannelFor(Alice)).Buttons.Select(b => b.CustomId).Should().NotContain(PanelRoutes.LinkPlex);
            (await HelpTopics(Alice)).Should().NotContain("plex");
        }

        [Test]
        public async Task with_a_plex_server_connected_both_come_back()
        {
            ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            await AddUser(Alice);

            PanelIn(ChannelFor(Alice)).Buttons.Select(b => b.CustomId).Should().Contain(PanelRoutes.LinkPlex);
            (await HelpTopics(Alice)).Should().Contain("plex");
        }

        [Test]
        public void the_admin_help_explains_why_media_server_buttons_are_hidden()
        {
            var mediaserver = HelpCatalog.AdminTopics.Single(t => t.Key == "mediaserver").Intro(HelpViewer.Everything);
            var access = HelpCatalog.AdminTopics.Single(t => t.Key == "access").Intro(HelpViewer.Everything);

            mediaserver.Should().Contain("only show for a media server you've connected").And.Contain("hidden for now");
            access.Should().Contain("no media server buttons").And.Contain("Link Plex is gone from member panels");
        }
    }
}
