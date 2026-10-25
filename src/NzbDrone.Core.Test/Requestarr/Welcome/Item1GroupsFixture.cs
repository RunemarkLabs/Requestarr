using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 1: two channel groups kept by the bot. Requestarr Admin holds the admin channel and the feed, which
    // follow the group's admin-only permissions; Requestarr Users holds the welcome, announcements and blocked channels
    // and every member's private channel. An existing server is moved over with its messages kept.
    [TestFixture]
    public class Item1GroupsFixture : WelcomeScenario
    {
        private FakeDiscordGuild.FakeChannel Group(string name) => Discord.Channels.Values.Single(c => c.IsCategory && c.Name == name);

        [Test]
        public void the_admin_group_holds_the_admin_channel_and_the_feed_with_the_group_permissions()
        {
            var group = Group(ChannelLayout.AdminCategoryName);
            var admin = Discord.ChannelNamed(ChannelLayout.AdminChannelName);
            var feed = Discord.ChannelNamed(ChannelLayout.FeedChannelName);

            Config.AdminCategoryId.Should().Be(group.Id.ToString());
            admin.CategoryId.Should().Be(group.Id);
            feed.CategoryId.Should().Be(group.Id);
            OverwriteFor(group, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel, "only the admin role and the bot see it");
            OverwriteFor(group, AdminRole).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(group, FakeDiscordGuild.Bot).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            admin.Overwrites.Should().BeEquivalentTo(group.Overwrites, "the channels follow the group");
            feed.Overwrites.Should().BeEquivalentTo(group.Overwrites);
        }

        [Test]
        public async Task the_users_group_holds_welcome_announcements_blocked_and_every_private_channel()
        {
            await AddUser(Alice);
            var group = Group(ChannelLayout.UsersCategoryName);

            foreach (var name in new[] { ChannelLayout.WelcomeChannelName, ChannelLayout.AnnouncementsChannelName, ChannelLayout.BlockedChannelName })
            {
                Discord.ChannelNamed(name).CategoryId.Should().Be(group.Id, name);
            }

            ChannelFor(Alice).CategoryId.Should().Be(group.Id);
            Config.RequestCategoryIds.Should().Equal(group.Id.ToString());
            OverwriteFor(group, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel, "new channels start hidden");
            OverwriteFor(ChannelFor(Alice), RoleOf(Alice)).Allow.Should().HaveFlag(ChannelPermissions.SendMessages, "private channels keep their own permissions on top");
        }

        [Test]
        public void the_welcome_channel_is_read_only_for_everyone_and_hidden_from_the_blocked_role()
        {
            var welcome = Discord.ChannelNamed(ChannelLayout.WelcomeChannelName);
            var everyone = OverwriteFor(welcome, FakeDiscordGuild.Guild);

            everyone.Allow.Should().Be(ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory, "everyone in the server reads it");
            everyone.Deny.Should().Be(ChannelPermissions.SendMessages | ChannelPermissions.Threads, "nobody posts or starts threads");
            OverwriteFor(welcome, AdminRole).Deny.Should().HaveFlag(ChannelPermissions.SendMessages, "only the bot posts");
            OverwriteFor(welcome, FakeDiscordGuild.Bot).Allow.Should().HaveFlag(ChannelPermissions.SendMessages);
            OverwriteFor(welcome, BlockedRole).Deny.Should().Be(ChannelPermissions.ViewChannel);
            welcome.Topic.Should().Be(DiscordText.WelcomeChannelTopic);
        }

        [Test]
        public void the_blocked_channel_is_read_only_and_only_the_blocked_role_sees_it()
        {
            var blocked = Discord.ChannelNamed(ChannelLayout.BlockedChannelName);

            OverwriteFor(blocked, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(blocked, BlockedRole).Allow.Should().Be(ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory);
            OverwriteFor(blocked, BlockedRole).Deny.Should().Be(ChannelPermissions.SendMessages | ChannelPermissions.Threads);
            Discord.Roles[BlockedRole].Should().Be(ChannelLayout.BlockedRoleName);
        }

        [Test]
        public async Task an_existing_server_moves_into_the_groups_with_its_messages_kept()
        {
            await AddUser(Alice);
            var aliceChannel = ChannelOf(Alice);
            var aliceMessages = ChannelFor(Alice).MessageIds.ToList();
            var usersGroup = ulong.Parse(Config.RequestCategoryIds.Single());

            // The layout before task 4e: the admin channel, the feed and announcements at the top, the private channels
            // in "Requestarr Requests", and no admin group, welcome or blocked channel.
            foreach (var name in new[] { ChannelLayout.AdminChannelName, ChannelLayout.FeedChannelName, ChannelLayout.AnnouncementsChannelName })
            {
                Discord.ChannelNamed(name).CategoryId = null;
            }

            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "an old approval card" });
            var adminMessages = Discord.Channels[AdminChannel].MessageIds.ToList();
            Discord.Channels[usersGroup].Name = ChannelLayout.OldRequestCategoryName;
            await Discord.DeleteChannelAsync(ulong.Parse(Config.AdminCategoryId));
            await Discord.DeleteChannelAsync(ulong.Parse(Config.WelcomeChannelId));
            await Discord.DeleteChannelAsync(ulong.Parse(Config.BlockedChannelId));
            var config = Config;
            config.AdminCategoryId = config.WelcomeChannelId = config.BlockedChannelId = config.BlockedRoleId = null;
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            Discord.Channels[usersGroup].Name.Should().Be(ChannelLayout.UsersCategoryName, "the request group becomes the users group in place");
            Discord.Channels.Values.Should().NotContain(c => c.IsCategory && c.Name.StartsWith(ChannelLayout.OldRequestCategoryName));
            ChannelOf(Alice).Should().Be(aliceChannel);
            ChannelFor(Alice).MessageIds.Should().Equal(aliceMessages, "nothing moved, nothing lost");
            Discord.ChannelNamed(ChannelLayout.AnnouncementsChannelName).CategoryId.Should().Be(usersGroup);
            Discord.ChannelNamed(ChannelLayout.WelcomeChannelName).CategoryId.Should().Be(usersGroup);
            Discord.ChannelNamed(ChannelLayout.BlockedChannelName).CategoryId.Should().Be(usersGroup);

            var adminGroup = Group(ChannelLayout.AdminCategoryName).Id;
            AdminChannel.Should().Be(Discord.ChannelNamed(ChannelLayout.AdminChannelName).Id, "the same admin channel");
            Discord.Channels[AdminChannel].CategoryId.Should().Be(adminGroup);
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(adminMessages);
            Discord.ChannelNamed(ChannelLayout.FeedChannelName).CategoryId.Should().Be(adminGroup);
        }

        [Test]
        public async Task deleted_groups_come_back_and_their_channels_return()
        {
            await AddUser(Alice);
            await Discord.DeleteChannelAsync(ulong.Parse(Config.AdminCategoryId));
            await Discord.DeleteChannelAsync(ulong.Parse(Config.RequestCategoryIds.Single()));

            foreach (var channel in Discord.Channels.Values.Where(c => !c.IsCategory))
            {
                channel.CategoryId = null;
            }

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            var users = Group(ChannelLayout.UsersCategoryName).Id;
            ChannelFor(Alice).CategoryId.Should().Be(users);
            Discord.ChannelNamed(ChannelLayout.WelcomeChannelName).CategoryId.Should().Be(users);
            Discord.Channels[AdminChannel].CategoryId.Should().Be(Group(ChannelLayout.AdminCategoryName).Id);
        }

        [Test]
        public async Task a_leftover_empty_request_group_is_removed()
        {
            var old = await Discord.CreateCategoryAsync(ChannelLayout.OldRequestCategoryName, ChannelLayout.Group(FakeDiscordGuild.Guild, FakeDiscordGuild.Bot, AdminRole));

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            Discord.Channels.Should().NotContainKey(old);
        }

        [Test]
        public async Task a_full_users_group_spills_into_the_next_and_keeps_its_four_channels()
        {
            var first = ulong.Parse(Config.RequestCategoryIds.Single());
            Discord.ChannelsIn(first).Should().HaveCount(4, "welcome, announcements, blocked and bug reports");

            for (var i = 4; i < 50; i++)
            {
                await Discord.CreateTextChannelAsync($"filler-{i}", first, null, System.Array.Empty<PermissionOverwrite>());
            }

            await AddUser(Alice);

            Discord.Channels[ChannelFor(Alice).CategoryId.Value].Name.Should().Be(ChannelLayout.CategoryName(2));
            Discord.Channels[ChannelFor(Alice).CategoryId.Value].Overwrites.Should().BeEquivalentTo(Discord.Channels[first].Overwrites, "the same group permissions");
        }
    }
}
