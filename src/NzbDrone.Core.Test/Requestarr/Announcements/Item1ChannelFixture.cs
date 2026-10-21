using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Announcements
{
    // Task 3d item 1: one announcements channel, readable by every onboarded member (through one shared
    // role) and read-only for them; admins and the bot post. Recreated if deleted; existing members join.
    [TestFixture]
    public class Item1ChannelFixture : DiscordScenario
    {
        private ulong MembersRole => ulong.Parse(Config.MembersRoleId);

        private ulong Channel => ulong.Parse(Config.AnnouncementsChannelId);

        private bool HasRole(ulong member) => Discord.Members[member].RoleIds.Contains(MembersRole);

        [Test]
        public void the_channel_and_the_members_role_exist_after_setup()
        {
            Discord.Roles[MembersRole].Should().Be(ChannelLayout.MembersRoleName);
            var channel = Discord.Channels[Channel];

            channel.Name.Should().Be(ChannelLayout.AnnouncementsChannelName);
            channel.CategoryId.Should().BeNull();
            channel.Topic.Should().Be(DiscordText.AnnouncementsChannelTopic);
        }

        [Test]
        public void members_read_but_cannot_post_admins_and_the_bot_post_and_nobody_else_sees_it()
        {
            var overwrites = Discord.Channels[Channel].Overwrites;

            overwrites.Single(o => o.TargetId == FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel, "@everyone doesn't see it");
            var members = overwrites.Single(o => o.TargetId == MembersRole);
            members.Allow.Should().Be(ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory);
            members.Deny.Should().Be(ChannelPermissions.SendMessages | ChannelPermissions.Threads, "read-only: no posts, no threads");
            overwrites.Single(o => o.TargetId == AdminRole).Allow.Should().HaveFlag(ChannelPermissions.SendMessages, "admins type their own announcements");
            overwrites.Single(o => o.TargetId == FakeDiscordGuild.Bot).Allow.Should().HaveFlag(ChannelPermissions.SendMessages);
        }

        [Test]
        public async Task a_deleted_channel_is_recreated_and_a_restart_reuses_both()
        {
            var role = MembersRole;
            var first = Channel;
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            Channel.Should().Be(first, "a restart reuses the channel");
            MembersRole.Should().Be(role);

            await Discord.DeleteChannelAsync(first);
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            Channel.Should().NotBe(first);
            Discord.Channels[Channel].Name.Should().Be(ChannelLayout.AnnouncementsChannelName);
            Discord.Roles.Values.Count(r => r == ChannelLayout.MembersRoleName).Should().Be(1, "one shared role, never one per member");
        }

        [Test]
        public async Task a_channel_whose_permissions_were_changed_is_made_read_only_again()
        {
            Discord.Channels[Channel].Overwrites.RemoveAll(o => o.TargetId == MembersRole);
            Discord.Channels[Channel].Overwrites.Add(new PermissionOverwrite(MembersRole, OverwriteTarget.Role, ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages, ChannelPermissions.None));

            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();

            Discord.Channels[Channel].Overwrites.Single(o => o.TargetId == MembersRole).Deny.Should().HaveFlag(ChannelPermissions.SendMessages);
        }

        [Test]
        public async Task members_gain_it_on_add_lose_it_on_suspend_and_remove_and_regain_it_on_unsuspend()
        {
            HasRole(Alice).Should().BeFalse();

            await AddUser(Alice);
            HasRole(Alice).Should().BeTrue("added");

            await Harness.SendAsync(AdminCommand("user suspend", Alice));
            HasRole(Alice).Should().BeFalse("suspended");
            Discord.Members[Alice].RoleIds.Should().Contain(ulong.Parse(UserFor(Alice).DiscordRoleId), "their own role stays while suspended");

            await Harness.SendAsync(AdminCommand("user unsuspend", Alice));
            HasRole(Alice).Should().BeTrue("unsuspended");

            await Harness.SendAsync(AdminCommand("user remove", Alice));
            UserFor(Alice).Status.Should().Be(UserStatus.Removed);
            HasRole(Alice).Should().BeFalse("removed");
        }

        [Test]
        public async Task a_member_who_leaves_the_server_is_suspended_without_errors()
        {
            await AddUser(Alice);
            Discord.RemoveMember(Alice);

            await Mocker.Resolve<IUserManagementService>().HandleMemberLeftAsync(Alice);

            UserFor(Alice).Status.Should().Be(UserStatus.Suspended);
        }

        [Test]
        public async Task on_start_existing_members_get_the_role_and_suspended_ones_lose_it()
        {
            // As on a server upgraded from before announcements: members exist without the role.
            await AddUser(Alice);
            await AddUser(Bob);
            await Harness.SendAsync(AdminCommand("user suspend", Bob));
            await Discord.RemoveRoleFromMemberAsync(Alice, MembersRole);
            await Discord.AddRoleToMemberAsync(Bob, MembersRole);

            await Mocker.Resolve<IAnnouncementService>().SyncMembersAsync();

            HasRole(Alice).Should().BeTrue();
            HasRole(Bob).Should().BeFalse();
            HasRole(Moderator).Should().BeFalse("only onboarded members");
        }
    }
}
