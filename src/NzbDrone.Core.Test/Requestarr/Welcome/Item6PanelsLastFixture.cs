using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 6: every panel is the newest message in its channel. When the bot posts something else there, the
    // panel is posted again below it (quietly: no ping, no notification) and the old one removed. Panels aren't pinned.
    [TestFixture]
    public class Item6PanelsLastFixture : WelcomeScenario
    {
        private IPanelService Panels => Mocker.Resolve<IPanelService>();

        [Test]
        public async Task the_admin_panel_moves_below_a_new_post()
        {
            var before = ulong.Parse(Config.AdminPanelMessageId);
            var alert = await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "an approval card" });

            await Panels.KeepLastAsync(AdminChannel);

            AdminRoom.MessageIds.Should().NotContain(before, "the old panel is removed");
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
            AdminRoom.MessageIds.Should().Contain(alert);
            PanelsIn(AdminRoom, PanelRoutes.AdminQueue).Should().Be(1);
            AdminRoom.Messages.Last().Quiet.Should().BeTrue("a moved panel pings and notifies nobody");
            AdminRoom.Pinned.Should().BeEmpty();
        }

        [Test]
        public async Task a_members_panel_moves_below_a_request_message()
        {
            await AddUser(Alice);
            await Discord.SendMessageAsync(ChannelOf(Alice), new InteractionReply { Content = "Dune (2021) is ready to watch" });

            await Panels.KeepLastAsync(ChannelOf(Alice));

            ChannelFor(Alice).MessageIds.Last().ToString().Should().Be(UserFor(Alice).PanelMessageId);
            ChannelFor(Alice).Messages.First().Content.Should().Be("Dune (2021) is ready to watch");
            PanelsIn(ChannelFor(Alice), PanelRoutes.MyRequests).Should().Be(1);
        }

        [Test]
        public async Task a_panel_already_last_stays_put()
        {
            var ids = AdminRoom.MessageIds.ToList();

            await Panels.KeepLastAsync(AdminChannel);
            await Panels.KeepLastAsync(WelcomeChannel);

            AdminRoom.MessageIds.Should().Equal(ids);
        }

        [Test]
        public async Task moving_a_panel_edits_nothing_so_no_message_is_marked_edited()
        {
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "an approval card" });
            AdminRoom.Edited.Clear();

            // The move, then the notice of the bot's own new panel: it's already last, so it's left alone.
            await Panels.KeepLastAsync(AdminChannel);
            await Panels.KeepLastAsync(AdminChannel);

            AdminRoom.Edited.Should().BeEmpty();
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
        }

        [Test]
        public async Task the_welcome_and_blocked_messages_stay_last_too()
        {
            await Discord.SendMessageAsync(WelcomeChannel, new InteractionReply { Content = "something" });
            await Discord.SendMessageAsync(BlockedChannel, new InteractionReply { Content = "something" });

            await Panels.KeepLastAsync(WelcomeChannel);
            await Panels.KeepLastAsync(BlockedChannel);

            Discord.Channels[WelcomeChannel].MessageIds.Last().ToString().Should().Be(Config.WelcomeMessageId);
            Discord.Channels[BlockedChannel].MessageIds.Last().ToString().Should().Be(Config.BlockedMessageId);
        }

        [Test]
        public async Task starting_up_puts_every_panel_last()
        {
            await AddUser(Alice);
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "posted while Requestarr was down" });
            await Discord.SendMessageAsync(ChannelOf(Alice), new InteractionReply { Content = "an older request message" });

            await Panels.EnsureAllAsync();

            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
            ChannelFor(Alice).MessageIds.Last().ToString().Should().Be(UserFor(Alice).PanelMessageId);
        }

        [Test]
        public async Task the_bots_posts_move_the_panel_shortly_after_and_its_own_panel_doesnt()
        {
            var post = await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "an alert" });

            Panels.NoticeBotMessage(AdminChannel, ulong.Parse(Config.AdminPanelMessageId));
            await Task.Delay(PanelService.MoveDelay + System.TimeSpan.FromSeconds(1));
            AdminRoom.MessageIds.Last().Should().Be(post, "the panel's own post moves nothing");

            Panels.NoticeBotMessage(AdminChannel, post);
            await Task.Delay(PanelService.MoveDelay + System.TimeSpan.FromSeconds(1));
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
        }

        [Test]
        public void channels_without_a_panel_are_left_alone()
        {
            var feed = Discord.ChannelNamed(ChannelLayout.FeedChannelName).Id;

            Panels.NoticeBotMessage(feed, 1);
            Panels.KeepLastAsync(feed).GetAwaiter().GetResult();

            Discord.Channels[feed].Messages.Should().BeEmpty();
        }
    }
}
