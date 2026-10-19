using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // Item 8: every action writes an audit entry (who, what, target, when).
    [TestFixture]
    public class Item8AuditFixture : DiscordScenario
    {
        [Test]
        public async Task every_action_should_be_audited_in_order()
        {
            await AddUser(Alice);
            await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "tv").WithOption("level", "auto"));
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 3L));
            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "a1"));
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "a2"));
            await Harness.SendAsync(FakeInteraction.Button("msreq-deny:1").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            await Harness.SendAsync(UserCommand("mediaserver request", Alice).WithOption("username", "a3"));
            await Harness.SendAsync(FakeInteraction.Button("msreq-approve:2").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            await Harness.SendAsync(AdminCommand("user suspend", Alice));
            await Harness.SendAsync(AdminCommand("user unsuspend", Alice));
            await Users.HandleMemberLeftAsync(Alice);
            await Harness.SendAsync(AdminCommand("user remove", Alice));

            var userActions = AuditTrail.Where(a => !a.Action.StartsWith("discord.")).Select(a => a.Action);

            userActions.Should().Equal(
                "user.add",
                "user.access",
                "user.quota",
                "mediaserver.link",
                "mediaserver.request",
                "mediaserver.deny",
                "mediaserver.request",
                "mediaserver.approve",
                "user.suspend",
                "user.unsuspend",
                "user.departed",
                "user.remove");
        }

        [Test]
        public async Task audit_entry_should_record_actor_target_time_and_detail()
        {
            await AddUser(Alice, movie: "auto");

            var entry = AuditTrail.Single(a => a.Action == "user.add");

            entry.Actor.Should().Be($"discord:{FakeDiscordGuild.Owner} (owner)");
            entry.TargetType.Should().Be("user");
            entry.TargetId.Should().Be(UserFor(Alice).Id.ToString());
            entry.Timestamp.Should().BeCloseTo(System.DateTime.UtcNow, System.TimeSpan.FromMinutes(1));
            entry.Detail.Should().Contain("Movie=AutoApprove");
        }

        [Test]
        public void guild_setup_should_be_audited()
        {
            AuditTrail.Select(a => a.Action).Should().Contain(new[] { "discord.admin-channel.create", "discord.category.create" });
        }

        [Test]
        public async Task rejected_commands_should_not_be_audited_as_actions()
        {
            await AddUser(Alice);
            var before = AuditTrail.Count;

            await Harness.SendAsync(FakeInteraction.SlashCommand("user remove").WithOption("member", Alice).By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            AuditTrail.Count.Should().Be(before);
        }
    }
}
