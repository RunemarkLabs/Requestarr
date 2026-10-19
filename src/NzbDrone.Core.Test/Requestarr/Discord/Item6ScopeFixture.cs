using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Discord
{
    // Item 6: admin commands only by admins in the admin channel; user commands only in the
    // caller's own channel; re-checked on every command and button.
    [TestFixture]
    public class Item6ScopeFixture : DiscordScenario
    {
        [SetUp]
        public async Task AddUsers()
        {
            await AddUser(Alice);
            await AddUser(Bob);
        }

        [Test]
        public async Task admin_command_by_non_admin_should_be_rejected()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin user suspend").WithOption("member", Bob).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            run.LastReply.Ephemeral.Should().BeTrue();
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task admin_command_in_a_user_channel_should_be_rejected_even_for_admin()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin user suspend").WithOption("member", Bob).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task admin_role_member_can_run_admin_commands_in_admin_channel()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin user suspend").WithOption("member", Bob).By(Moderator).WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("bob"));
        }

        [Test]
        public async Task commands_from_another_server_should_be_rejected()
        {
            var run = await Harness.SendAsync(AdminCommand("user info", Alice).InGuild(123));

            run.LastReply.Content.Should().Be(DiscordText.WrongServer);
        }

        [Test]
        public async Task user_command_in_another_users_channel_should_be_rejected()
        {
            var run = await Harness.SendAsync(FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "x").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)));

            run.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task user_command_in_admin_channel_by_admin_should_point_to_admin_command()
        {
            var run = await Harness.SendAsync(FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "x").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminUseUserCommand);
        }

        [Test]
        public async Task user_command_by_someone_not_added_should_be_rejected()
        {
            var run = await Harness.SendAsync(FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "x").By(Moderator).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.NotAUser);
        }

        [Test]
        public async Task suspended_user_commands_should_be_rejected()
        {
            await Harness.SendAsync(AdminCommand("user suspend", Alice));

            var run = await Harness.SendAsync(PlexForm(Alice, "x"));

            run.LastReply.Content.Should().Be(DiscordText.Suspended);
        }

        [Test]
        public async Task approve_button_clicked_by_user_should_be_rejected_despite_valid_custom_id()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            var run = await Harness.SendAsync(FakeInteraction.Button("msreq-approve:1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
        }

        [Test]
        public async Task approve_button_outside_admin_channel_should_be_rejected()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            var run = await Harness.SendAsync(FakeInteraction.Button("msreq-approve:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
        }
    }
}
