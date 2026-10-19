using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Discord
{
    // Item 5: a member leaving is suspended automatically and can be restored after rejoining.
    [TestFixture]
    public class Item5DeparturesFixture : DiscordScenario
    {
        [Test]
        public async Task leaving_member_should_be_suspended_and_channel_locked()
        {
            await AddUser(Alice);
            Discord.RemoveMember(Alice);

            await Users.HandleMemberLeftAsync(Alice);

            UserFor(Alice).Status.Should().Be(UserStatus.Suspended);
            OverwriteFor(ChannelFor(Alice), RoleOf(Alice)).Deny.Should().HaveFlag(ChannelPermissions.SendMessages);
            ChannelFor(Alice).Messages.Last().Content.Should().Be(DiscordText.MemberLeftNotice);
            AuditTrail.Last().Action.Should().Be("user.departed");
            AuditTrail.Last().Actor.Should().Be("system");
        }

        [Test]
        public async Task history_should_be_kept_after_departure()
        {
            await AddUser(Alice);
            var id = UserFor(Alice).Id;

            await Users.HandleMemberLeftAsync(Alice);

            UserFor(Alice).Id.Should().Be(id);
            Discord.Channels.Should().ContainKey(ChannelOf(Alice));
        }

        [Test]
        public async Task unsuspend_should_fail_while_member_is_away_and_restore_role_after_rejoin()
        {
            await AddUser(Alice);
            var role = RoleOf(Alice);
            Discord.RemoveMember(Alice);
            await Users.HandleMemberLeftAsync(Alice);

            (await Harness.SendAsync(AdminCommand("user unsuspend", Alice))).LastReply.Content.Should().Be(DiscordText.NotInServer);

            Discord.AddMember(Alice, "alice");
            (await Harness.SendAsync(AdminCommand("user unsuspend", Alice))).LastReply.Content.Should().Be(DiscordText.UserUnsuspendedDone("alice"));

            Discord.Members[Alice].RoleIds.Should().Contain(role);
            UserFor(Alice).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task departure_of_unknown_or_already_suspended_member_should_do_nothing()
        {
            await Users.HandleMemberLeftAsync(Bob);
            AuditTrail.Should().NotContain(a => a.Action == "user.departed");

            await AddUser(Alice);
            await Harness.SendAsync(AdminCommand("user suspend", Alice));
            await Users.HandleMemberLeftAsync(Alice);

            AuditTrail.Count(a => a.Action == "user.departed").Should().Be(0);
        }
    }
}
