using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Requests;

namespace Requestarr.Core.Test.CommandSurface
{
    // Item 4: every admin command lives under /admin, hidden from non-admins; old commands are gone.
    [TestFixture]
    public class Item4AdminPrefixFixture : RequestScenario
    {
        [Test]
        public void exactly_request_and_admin_are_registered()
        {
            var commands = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().ToDictionary(c => c.Name.Value);

            commands.Keys.Should().BeEquivalentTo("request", "help", "admin");
            commands["admin"].DefaultMemberPermissions.Value.Should().Be(GuildPermission.ManageGuild);
            commands["request"].DefaultMemberPermissions.IsSpecified.Should().BeFalse();
            commands["request"].Options.Value.Select(o => o.Name).Should().Equal("type", "query");
            commands["request"].Options.Value.First().IsAutocomplete.Should().BeTrue();
        }

        [Test]
        public void admin_tree_holds_every_admin_command()
        {
            var admin = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().Single(c => c.Name.Value == "admin");

            var paths = new List<string>();

            foreach (var option in admin.Options.Value)
            {
                if (option.Type == ApplicationCommandOptionType.SubCommandGroup)
                {
                    paths.AddRange(option.Options.Select(o => $"{option.Name} {o.Name}"));
                }
                else
                {
                    paths.Add(option.Name);
                }
            }

            var expected = new[]
            {
                "user add", "user remove", "user suspend", "user unsuspend", "user access", "user quota", "user info", "user mediaserver",
                "help", "queue", "approve", "available", "cancel", "deny", "mediaserver approve", "mediaserver deny"
            };

            paths.Should().BeEquivalentTo(expected);
        }

        [Test]
        public async Task admin_commands_work_under_the_prefix()
        {
            await RequestMovie(Alice, Dune);
            await RequestMovie(Bob, DuneTwo);
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            (await Admin("queue")).LastReply.Content.Should().Contain("#1 ·").And.Contain("#2 ·");
            (await Harness.SendAsync(AdminSlash("approve").WithOption("id", 1L))).LastReply.Content.Should().StartWith("Approved request #1");
            (await Harness.SendAsync(AdminSlash("deny").WithOption("id", 2L).WithOption("reason", "no"))).LastReply.Content.Should().Be(DiscordText.AdminDenied(RequestStore.Get(2)));
            (await Harness.SendAsync(AdminSlash("mediaserver approve").WithOption("id", 1L))).LastReply.Content.Should().Contain("approved");
            (await Harness.SendAsync(AdminCommand("user info", Alice))).LastReply.Content.Should().Contain("Plex `alice_plex`");
        }

        [TestCase("user add")]
        [TestCase("queue")]
        [TestCase("approve")]
        [TestCase("requests")]
        [TestCase("quota")]
        [TestCase("mediaserver request")]
        [TestCase("request movie")]
        public async Task old_top_level_commands_are_no_longer_handled(string old)
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand(old).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.Outcome.Should().Be(DispatchOutcome.Unhandled);
            run.LastReply.Content.Should().Be(InteractionDispatcher.UnknownInteractionMessage);
        }

        [Test]
        public async Task buttons_on_cards_posted_before_the_change_still_work()
        {
            await RequestMovie(Alice, Dune);
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            (await AdminButton("req-approve:1")).LastReply.Content.Should().Contain("approved");
            (await AdminButton("msreq-deny:1")).LastReply.Content.Should().Contain("denied");
            Mocker.Resolve<IMediaServerAccessRequestRepository>().Get(1).Status.Should().Be(AccessRequestStatus.Denied);
            RequestStore.Get(1).Status.Should().Be(RequestStatus.Approved);
        }
    }
}
