using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 6: server-side admin and channel checks unchanged under the new surface.
    [TestFixture]
    public class Item6EnforcementFixture : RequestScenario
    {
        [Test]
        public async Task request_in_someone_elses_channel_is_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)));

            run.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task request_in_admin_channel_by_a_user_is_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task admin_command_from_a_non_admin_is_refused_even_if_discord_showed_it()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin user suspend").WithOption("member", Bob).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task admin_command_outside_the_admin_channel_is_refused_for_the_owner_too()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("admin queue").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
        }

        [Test]
        public async Task suspended_user_cannot_request_or_use_the_panel()
        {
            await Harness.SendAsync(AdminCommand("user suspend", Alice));

            (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply.Content.Should().Be(DiscordText.Suspended);
            (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyRequests))).LastReply.Content.Should().Be(DiscordText.Suspended);
            (await Harness.SendAsync(PlexForm(Alice, "x"))).LastReply.Content.Should().Be(DiscordText.Suspended);
        }

        [Test]
        public async Task commands_from_another_server_are_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(Alice).InGuild(42).InChannel(ChannelOf(Alice)));

            run.LastReply.Content.Should().Be(DiscordText.WrongServer);
        }
    }
}
