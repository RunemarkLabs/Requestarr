using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Discord
{
    // Item 3: /user add, remove, suspend, unsuspend, access, quota, info.
    [TestFixture]
    public class Item3OnboardingFixture : DiscordScenario
    {
        [Test]
        public async Task add_should_create_user_identity_role_and_private_channel()
        {
            var run = await AddUser(Alice);

            run.Outcome.Should().Be(DispatchOutcome.Handled);
            run.Responder.Responses[0].Kind.Should().Be(ResponseKind.Deferred);
            run.LastReply.Content.Should().StartWith("Added alice.");

            var user = UserFor(Alice);
            user.Status.Should().Be(UserStatus.Active);
            Discord.Roles[RoleOf(Alice)].Should().Be("requestarr-alice");
            Discord.Members[Alice].RoleIds.Should().Contain(RoleOf(Alice));

            var channel = ChannelFor(Alice);
            channel.Name.Should().Be("requests-alice");
            channel.CategoryId.Should().Be(ulong.Parse(Config.RequestCategoryIds.Single()));
        }

        [Test]
        public async Task user_channel_should_be_visible_only_to_user_role_admin_role_and_bot()
        {
            await AddUser(Alice);
            var channel = ChannelFor(Alice);

            OverwriteFor(channel, FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(channel, RoleOf(Alice)).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages);
            OverwriteFor(channel, AdminRole).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(channel, FakeDiscordGuild.Bot).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages);
            channel.Overwrites.Should().HaveCount(4);
        }

        [Test]
        public async Task add_should_record_access_per_content_type_with_defaults()
        {
            await AddUser(Alice, movie: "auto");

            var access = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Alice).Id).ToDictionary(a => a.ContentType, a => a.State);

            access[ContentType.Movie].Should().Be(AccessState.AutoApprove);
            access[ContentType.Tv].Should().Be(AccessState.NeedsApproval);
            access[ContentType.Music].Should().Be(AccessState.Off);
            access[ContentType.Comic].Should().Be(AccessState.Off);
            access[ContentType.Book].Should().Be(AccessState.Off);
        }

        [Test]
        public async Task add_should_post_welcome_in_new_channel()
        {
            await AddUser(Alice, music: "approval");

            // One message: the pinned panel is the welcome, and it never lists music before Lidarr exists.
            ChannelFor(Alice).Messages.Should().ContainSingle();
            var welcome = ChannelFor(Alice).PinnedMessage.Content;
            welcome.Should().Contain($"<@{Alice}>").And.Contain("Movies (needs approval)").And.NotContain("Music").And.NotContain("Comics");
        }

        [Test]
        public async Task add_should_refuse_existing_user()
        {
            await AddUser(Alice);

            var run = await AddUser(Alice);

            run.LastReply.Content.Should().Be(DiscordText.AlreadyUser("alice", UserStatus.Active));
            Discord.Roles.Values.Count(r => r == "requestarr-alice").Should().Be(1);
        }

        [Test]
        public async Task add_should_refuse_bots_and_non_members()
        {
            (await AddUser(FakeDiscordGuild.Bot)).LastReply.Content.Should().Be(DiscordText.CannotAddBot);
            (await AddUser(123456789)).LastReply.Content.Should().Be(DiscordText.MemberNotFound);
        }

        [Test]
        public async Task add_failure_should_roll_back_discord_changes()
        {
            Discord.FailNextChannelCreate = true;
            var rolesBefore = Discord.Roles.Count;

            var run = await AddUser(Alice);

            run.LastReply.Content.Should().Be(DiscordText.AddFailed);
            Discord.Roles.Count.Should().Be(rolesBefore);
            UserFor(Alice).Should().BeNull();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public async Task remove_should_archive_channel_delete_role_and_keep_history()
        {
            await AddUser(Alice);
            var roleId = RoleOf(Alice);
            var channelId = ChannelOf(Alice);

            var run = await Harness.SendAsync(AdminCommand("user remove", Alice));

            run.LastReply.Content.Should().Be(DiscordText.UserRemovedDone("alice"));
            Discord.Roles.Should().NotContainKey(roleId);
            Discord.Channels[channelId].Name.Should().Be("archived-requests-alice");
            Discord.Channels[channelId].Overwrites.Should().NotContain(o => o.TargetId == roleId);
            OverwriteFor(Discord.Channels[channelId], FakeDiscordGuild.Guild).Deny.Should().HaveFlag(ChannelPermissions.ViewChannel);

            var user = UserFor(Alice);
            user.Status.Should().Be(UserStatus.Removed);
            user.PrivateChannelId.Should().Be(channelId.ToString());
        }

        [Test]
        public async Task removed_user_can_be_added_again()
        {
            await AddUser(Alice);
            var id = UserFor(Alice).Id;
            await Harness.SendAsync(AdminCommand("user remove", Alice));

            var run = await AddUser(Alice);

            run.LastReply.Content.Should().StartWith("Added alice.");
            UserFor(Alice).Id.Should().Be(id);
            UserFor(Alice).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task suspend_should_lock_channel_and_unsuspend_should_restore_it()
        {
            await AddUser(Alice);

            (await Harness.SendAsync(AdminCommand("user suspend", Alice))).LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("alice"));
            UserFor(Alice).Status.Should().Be(UserStatus.Suspended);
            var locked = OverwriteFor(ChannelFor(Alice), RoleOf(Alice));
            locked.Deny.Should().HaveFlag(ChannelPermissions.SendMessages);
            locked.Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            ChannelFor(Alice).Messages.Last().Content.Should().Be(DiscordText.ChannelLockedNotice);

            (await Harness.SendAsync(AdminCommand("user unsuspend", Alice))).LastReply.Content.Should().Be(DiscordText.UserUnsuspendedDone("alice"));
            UserFor(Alice).Status.Should().Be(UserStatus.Active);
            OverwriteFor(ChannelFor(Alice), RoleOf(Alice)).Allow.Should().HaveFlag(ChannelPermissions.SendMessages);
            OverwriteFor(ChannelFor(Alice), RoleOf(Alice)).Deny.Should().Be(ChannelPermissions.None);
        }

        [Test]
        public async Task suspend_and_unsuspend_should_reject_wrong_state()
        {
            await AddUser(Alice);

            (await Harness.SendAsync(AdminCommand("user unsuspend", Alice))).LastReply.Content.Should().Be(DiscordText.NotSuspended("alice"));
            await Harness.SendAsync(AdminCommand("user suspend", Alice));
            (await Harness.SendAsync(AdminCommand("user suspend", Alice))).LastReply.Content.Should().Be(DiscordText.AlreadySuspended("alice"));
        }

        [TestCase("movie", "off", ContentType.Movie, AccessState.Off)]
        [TestCase("tv", "auto", ContentType.Tv, AccessState.AutoApprove)]
        [TestCase("music", "approval", ContentType.Music, AccessState.NeedsApproval)]
        public async Task access_should_change_one_content_type(string type, string level, ContentType contentType, AccessState expected)
        {
            await AddUser(Alice);

            var run = await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", type).WithOption("level", level));

            run.LastReply.Content.Should().Be(DiscordText.AccessChanged("alice", contentType, expected));
            Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Alice).Id).Single(a => a.ContentType == contentType).State.Should().Be(expected);
        }

        [Test]
        public async Task access_should_not_offer_comics_or_books()
        {
            await AddUser(Alice);

            (await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "comic").WithOption("level", "auto"))).LastReply.Content.Should().Be(DiscordText.TypeNotOffered);
            Users.SetAccess(Alice, ContentType.Book, AccessState.AutoApprove, Audit.Actor.System).Message.Should().Be(DiscordText.TypeNotOffered);
        }

        [Test]
        public async Task quota_should_set_and_clear()
        {
            await AddUser(Alice);

            (await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 5L).WithOption("days", 14L)))
                .LastReply.Content.Should().Be(DiscordText.QuotaChanged("alice", ContentType.Movie, 5, 14));

            var row = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Alice).Id).Single(a => a.ContentType == ContentType.Movie);
            row.QuotaLimit.Should().Be(5);
            row.QuotaWindow.Should().Be(TimeSpan.FromDays(14));

            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 0L));

            row = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(UserFor(Alice).Id).Single(a => a.ContentType == ContentType.Movie);
            row.QuotaLimit.Should().BeNull();
            row.QuotaWindow.Should().BeNull();
        }

        [Test]
        public async Task info_should_show_status_channel_access_and_media_server()
        {
            await AddUser(Alice, tv: "auto");
            Users.LinkMediaServer(Alice, MediaServers.MediaServerProvider.Plex, "alice_plex", Audit.Actor.System);

            var info = (await Harness.SendAsync(AdminCommand("user info", Alice))).LastReply.Content;

            info.Should().Contain("Status: active").And.Contain($"<#{ChannelOf(Alice)}>").And.Contain("TV: auto-approve").And.Contain("Plex `alice_plex`");
        }

        [Test]
        public async Task commands_on_unknown_or_removed_users_should_explain()
        {
            // Someone not added yet: their card says so and offers only Add.
            var card = (await Harness.SendAsync(AdminCommand("user info", Bob))).LastReply;
            card.Content.Should().Be(DiscordText.MemberNotAdded("bob", Bob));
            card.Buttons.Select(b => b.CustomId).Should().Equal($"adm-add:{Bob}");
            (await Harness.SendAsync(AdminCommand("user suspend", Bob))).LastReply.Content.Should().Be(DiscordText.NotARequestarrUser);

            await AddUser(Bob);
            await Harness.SendAsync(AdminCommand("user remove", Bob));
            (await Harness.SendAsync(AdminCommand("user suspend", Bob))).LastReply.Content.Should().Be(DiscordText.UserRemoved);
        }
    }
}
