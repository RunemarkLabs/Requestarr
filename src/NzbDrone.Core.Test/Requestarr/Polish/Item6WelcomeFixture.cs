using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Polish
{
    // Item 6: one pinned message is the welcome; it shows real access and buttons, no how-to text.
    [TestFixture]
    public class Item6WelcomeFixture : PolishScenario
    {
        private string Panel => ChannelFor(Alice).PinnedMessage.Content;

        [Test]
        public void the_welcome_is_the_one_pinned_panel()
        {
            ChannelFor(Alice).Messages.Should().ContainSingle();
            ChannelFor(Alice).Pinned.Single().ToString().Should().Be(UserFor(Alice).PanelMessageId);
            Panel.Should().Contain($"Welcome <@{Alice}>!").And.Contain("You can request: Movies (needs approval), TV (needs approval).");
        }

        [Test]
        public async Task the_access_list_follows_real_access_and_never_lists_music_yet()
        {
            await SetAccess(Alice, "music", "auto");
            Panel.Should().NotContain("Music");

            await SetAccess(Alice, "tv", "off");
            Panel.Should().Contain("You can request: Movies (needs approval).");

            await SetAccess(Alice, "movie", "auto");
            Panel.Should().Contain("You can request: Movies (auto-approve).");

            await SetAccess(Alice, "movie", "off");
            Panel.Should().Contain(DiscordText.NothingToRequest);
            ChannelFor(Alice).Messages.Should().ContainSingle();
        }

        [Test]
        public async Task the_panel_holds_no_how_to_instructions()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            Panel.Should().NotContain("/request").And.NotContain("tmdb:").And.NotContain("tvdb:");
            Discord.Channels[AdminChannel].PinnedMessage.Content.Should().NotContain("/admin");
        }

        [Test]
        public async Task both_panels_have_a_help_button()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            ChannelFor(Alice).PinnedMessage.Buttons.Select(b => b.CustomId).Should().Contain(PanelRoutes.Help);
            Discord.Channels[AdminChannel].PinnedMessage.Buttons.Select(b => b.CustomId).Should().Contain(PanelRoutes.AdminHelp);
        }

        [Test]
        public async Task existing_channels_get_the_new_panel_in_place_on_start()
        {
            var id = ulong.Parse(UserFor(Alice).PanelMessageId);
            await Discord.EditMessageAsync(ChannelOf(Alice), id, new InteractionReply { Content = "old panel with Type `/request` instructions" });

            await Mocker.Resolve<IPanelService>().EnsureAllAsync();

            ChannelFor(Alice).Messages.Should().ContainSingle();
            Panel.Should().NotContain("/request").And.Contain("You can request");
        }
    }
}
