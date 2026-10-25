using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e, from the owner watching the dev server: the bot recognises its own panels, groups and channels by what
    // they are, not only by the IDs in its database, so a restart with a new database or a reset never makes a second
    // one; a member who returns gets their one channel back; loose channels go back into their group; one block notice
    // per person; and moving a panel never undoes a change to the member made meanwhile.
    [TestFixture]
    public class Item10NoDuplicatesFixture : WelcomeScenario
    {
        private int Panels(ulong channel, string route) => PanelsIn(Discord.Channels[channel], route);

        private int ChannelsOf(string slug) => Discord.Channels.Values.Count(c => !c.IsCategory && c.Name.EndsWith("requests-" + slug));

        // A new database on the same server: everything Requestarr stored is gone, the server is as it was.
        private async Task StartWithANewDatabase()
        {
            Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
            Mocker.Resolve<IRequestarrUserRepository>().Purge();
            Mocker.Resolve<IUserIdentityRepository>().Purge();
            Mocker.Resolve<IUserMediaAccessRepository>().Purge();
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
        }

        [Test]
        public async Task a_new_database_finds_the_panels_groups_and_channels_already_there()
        {
            var channels = Discord.Channels.Count;

            await StartWithANewDatabase();
            await StartWithANewDatabase();

            Discord.Channels.Count.Should().Be(channels, "no second group or channel");
            Panels(AdminChannel, PanelRoutes.AdminQueue).Should().Be(1);
            Panels(WelcomeChannel, WelcomeRoutes.Request).Should().Be(1);
            Discord.Channels[BlockedChannel].Messages.Count(m => m.Cards.Any(c => c.Text == DiscordText.BlockedMessage)).Should().Be(1);
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId, "and it's the newest message");
        }

        [Test]
        public async Task copies_of_a_panel_are_removed_leaving_exactly_one()
        {
            await Discord.SendMessageAsync(AdminChannel, PanelService.AdminPanel());
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "a version card" });
            await Discord.SendMessageAsync(AdminChannel, PanelService.AdminPanel());

            await Mocker.Resolve<IPanelService>().KeepLastAsync(AdminChannel);

            Panels(AdminChannel, PanelRoutes.AdminQueue).Should().Be(1);
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
            AdminRoom.Messages[^2].Content.Should().Be("a version card", "the card sits right above the one panel");
        }

        [Test]
        public async Task a_member_added_again_with_a_new_database_gets_their_one_channel_back()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            var role = RoleOf(Alice);

            await StartWithANewDatabase();
            await AddUser(Alice);

            ChannelOf(Alice).Should().Be(channel);
            RoleOf(Alice).Should().Be(role);
            ChannelsOf("alice").Should().Be(1);
            PanelsIn(ChannelFor(Alice), PanelRoutes.MyRequests).Should().Be(1);
        }

        [Test]
        public async Task a_removed_member_who_returns_gets_their_archived_channel_back_live()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            await Harness.SendAsync(AdminCommand("user remove", Alice));
            Discord.Channels[channel].Name.Should().StartWith(ChannelLayout.ArchivedPrefix);

            await AskForAccess(Alice, null, "movie");
            var approved = await Approve(LastRequest.Id);

            ChannelOf(Alice).Should().Be(channel, "their one channel, messages kept");
            Discord.Channels[channel].Name.Should().Be("requests-alice");
            Discord.Channels[channel].CategoryId.Should().Be(ulong.Parse(Config.RequestCategoryIds[0]));
            OverwriteFor(Discord.Channels[channel], RoleOf(Alice)).Allow.Should().HaveFlag(ChannelPermissions.SendMessages);
            approved.LastReply.Content.Should().Contain($"<#{channel}>");
            ChannelsOf("alice").Should().Be(1);
        }

        [Test]
        public async Task the_archived_channel_is_found_by_name_too()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            await Harness.SendAsync(AdminCommand("user remove", Alice));
            Mocker.Resolve<IRequestarrUserRepository>().Purge();
            Mocker.Resolve<IUserIdentityRepository>().Purge();

            await AddUser(Alice);

            ChannelOf(Alice).Should().Be(channel);
            Discord.Channels[channel].Name.Should().Be("requests-alice");
        }

        [Test]
        public async Task channels_left_loose_when_a_group_was_deleted_go_back_in()
        {
            await AddUser(Alice);
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            var loose = await Discord.CreateTextChannelAsync("requests-carol", null, null, System.Array.Empty<PermissionOverwrite>());
            await Discord.DeleteChannelAsync(ulong.Parse(Config.RequestCategoryIds[0]));
            await Discord.DeleteChannelAsync(ulong.Parse(Config.AdminCategoryId));

            foreach (var channel in Discord.Channels.Values.Where(c => !c.IsCategory && c.CategoryId.HasValue && !Discord.Channels.ContainsKey(c.CategoryId.Value)))
            {
                channel.CategoryId = null;
            }

            await StartWithANewDatabase();

            var users = ulong.Parse(Config.RequestCategoryIds[0]);
            Discord.Channels.Values.Where(c => !c.IsCategory).Should().OnlyContain(c => c.CategoryId != null, "nothing left at the top level");
            Discord.ChannelNamed("requests-alice").CategoryId.Should().Be(users, "known by name even with a new database");
            Discord.Channels[loose].CategoryId.Should().Be(users);
            Discord.Channels[AdminChannel].CategoryId.Should().Be(ulong.Parse(Config.AdminCategoryId));
        }

        [Test]
        public async Task a_second_block_replaces_the_first_notice_and_unblocking_closes_it()
        {
            await AskForAccess(Carol, null, "movie");

            for (var round = 0; round < 2; round++)
            {
                for (var i = 0; i < 15; i++)
                {
                    await Press(Carol);
                }

                AdminRoom.Messages.Count(m => m.Content != null && m.Content.StartsWith("🚫") && m.Content.Contains($"<@{Carol}>")).Should().Be(1, "one notice per person");
                await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{AccessRoutes.Unblock}:{Carol}")));
            }

            AdminRoom.Messages.Should().ContainSingle(m => m.Content == DiscordText.Unblocked(Carol), "the notice now says it was lifted");
            AdminRoom.Messages.Should().NotContain(m => m.Buttons.Any(b => b.CustomId == $"{AccessRoutes.Unblock}:{Carol}"));
        }

        [Test]
        public async Task moving_a_panel_never_undoes_a_change_made_meanwhile()
        {
            await AddUser(Alice);
            await Discord.SendMessageAsync(ChannelOf(Alice), new InteractionReply { Content = "a request message" });
            var stale = UserFor(Alice);

            await Harness.SendAsync(AdminCommand("user suspend", Alice));
            await Mocker.Resolve<IPanelService>().KeepLastAsync(ulong.Parse(stale.PrivateChannelId));

            UserFor(Alice).Status.Should().Be(UserStatus.Suspended);
        }
    }
}
