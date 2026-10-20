using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Polish
{
    // Item 7: separate user and admin help trees, private, menu-driven, filtered, built from definitions.
    [TestFixture]
    public class Item7HelpFixture : PolishScenario
    {
        private static readonly string[] AllUserTopics = { "request", "seasons", "anime", "follow", "mine", "limits", "plex" };
        private static readonly string[] AllAdminTopics = { "onboarding", "approvals", "limits", "mediaserver", "anime", "visibility" };

        private static string[] Topics(InteractionRun run) => run.LastReply.SelectMenu.Options.Select(o => o.Value).ToArray();

        private Task<InteractionRun> UserHelp(ulong user) => Harness.SendAsync(UserCommand("help", user));

        private Task<InteractionRun> AdminHelp() => Admin("help");

        [Test]
        public async Task user_help_is_private_with_a_topic_menu()
        {
            var run = await UserHelp(Alice);

            run.Responder.Responses.Single().Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().StartWith("**Requestarr help**").And.Contain("movies and tv");
            run.LastReply.SelectMenu.CustomId.Should().Be("help:user");
            Topics(run).Should().Equal(AllUserTopics);
        }

        [Test]
        public async Task the_panel_help_button_opens_the_same_help()
        {
            Topics(await Harness.SendAsync(PanelButton(Alice, PanelRoutes.Help))).Should().Equal(AllUserTopics);
        }

        [Test]
        public async Task topics_a_user_cannot_use_are_not_shown()
        {
            await SetAccess(Alice, "tv", "off");
            Topics(await UserHelp(Alice)).Should().Equal("request", "follow", "mine", "limits", "plex");

            await Harness.SendAsync(AdminCommand("user mediaserver", Alice).WithOption("service", "plex").WithOption("username", "alice_plex"));
            Topics(await UserHelp(Alice)).Should().NotContain("plex");

            await SetAccess(Alice, "movie", "off");
            Topics(await UserHelp(Alice)).Should().Equal("mine", "limits");
        }

        [Test]
        public async Task the_request_topic_only_mentions_types_the_user_has()
        {
            await SetAccess(Alice, "tv", "off");

            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "request").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            topic.LastReply.Content.Should().Contain("tmdb:603").And.NotContain("tvdb:");
        }

        [Test]
        public async Task topics_open_in_place_with_back_to_the_overview()
        {
            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "seasons").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            topic.Responder.Responses.Single().Kind.Should().Be(ResponseKind.MessageUpdate);
            topic.LastReply.Content.Should().StartWith("**TV seasons**").And.Contain(DiscordText.AllSeasons).And.Contain(DiscordText.ChooseSeasonsLabel);
            topic.LastReply.Buttons.Single().CustomId.Should().Be("help-home:user");

            var home = await Click(Alice, "help-home:user");
            home.LastReply.Content.Should().StartWith("**Requestarr help**");
            home.LastReply.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task an_admin_running_help_only_sees_user_topics()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("help").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            Topics(run).Should().Equal(AllUserTopics);
            Topics(run).Should().NotContain(AllAdminTopics.Except(new[] { "limits", "anime" }));
            run.LastReply.SelectMenu.Options.Select(o => o.Label).Should().NotContain(HelpCatalog.AdminTopics.Where(t => t.Key != "limits").Select(t => t.Title));
            run.LastReply.SelectMenu.CustomId.Should().Be("help:user");
        }

        [Test]
        public async Task admin_help_is_admin_only_and_admin_channel_only()
        {
            (await Harness.SendAsync(UserCommand("admin help", Alice))).LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.SlashCommand("admin help").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice))))
                .LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
            (await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "onboarding").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice))))
                .LastReply.Content.Should().Be(DiscordText.AdminOnly);

            var run = await AdminHelp();
            run.LastReply.Ephemeral.Should().BeTrue();
            Topics(run).Should().Equal(AllAdminTopics.Append(HelpCatalog.UsersBranchKey));
        }

        [Test]
        public async Task the_admin_panel_help_button_opens_admin_help()
        {
            Topics(await AdminButton(PanelRoutes.AdminHelp)).Should().Equal(AllAdminTopics.Append(HelpCatalog.UsersBranchKey));
        }

        [Test]
        public async Task what_users_see_shows_every_user_topic_with_back_to_admin_help()
        {
            var branch = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", HelpCatalog.UsersBranchKey).By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            branch.LastReply.SelectMenu.CustomId.Should().Be("help:adminuser");
            Topics(branch).Should().Equal(AllUserTopics);
            branch.LastReply.Buttons.Single().CustomId.Should().Be("help-home:admin");

            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:adminuser", "plex").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            topic.LastReply.Content.Should().StartWith("**Linking Plex**");
            topic.LastReply.Buttons.Single().CustomId.Should().Be("help-home:adminuser");
        }

        [Test]
        public async Task topic_bodies_are_built_from_the_command_definitions()
        {
            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "onboarding").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            topic.LastReply.Content.Should().Contain($"`/admin user add member [movie] [tv] [music]`: {DiscordText.Commands.UserAdd}")
                .And.Contain($"**{DiscordText.PanelUsers}** on the admin panel");
        }

        [Test]
        public void every_registered_command_and_panel_button_has_a_help_topic()
        {
            var topics = HelpCatalog.UserTopics.Concat(HelpCatalog.AdminTopics).ToList();
            var commands = topics.SelectMany(t => t.Commands).Concat(HelpCatalog.UserEntryCommands).Concat(HelpCatalog.AdminEntryCommands).ToHashSet();
            var buttons = topics.SelectMany(t => t.Buttons).Concat(HelpCatalog.UserEntryButtons).Concat(HelpCatalog.AdminEntryButtons).Select(b => b.Route).ToHashSet();
            var registered = DiscordCommandDefinitions.Leaves().Select(l => l.Path).ToList();

            registered.Where(p => !commands.Contains(p)).Should().BeEmpty("every command and subcommand needs a help topic");
            PanelButtons.User.Concat(PanelButtons.Admin).Select(b => b.Route).Where(r => !buttons.Contains(r)).Should().BeEmpty("every panel button needs a help topic");
            commands.Should().BeSubsetOf(registered, "help must not describe commands that no longer exist");
        }

        [Test]
        public async Task the_credits_topic_with_the_tmdb_notice_and_logo_only_appears_with_a_key()
        {
            Topics(await UserHelp(Alice)).Should().NotContain("credits");
            Topics(await AdminHelp()).Should().NotContain("credits");

            TmdbKey.Set("test-key");

            Topics(await UserHelp(Alice)).Should().EndWith("credits");
            Topics(await AdminHelp()).Should().Contain("credits");

            var credits = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "credits").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            credits.LastReply.Content.Should().Contain("is not endorsed, certified, or otherwise approved by TMDB");
            credits.LastReply.Embeds.Single().ThumbnailUrl.Should().Be(DiscordText.TmdbLogoUrl);
            credits.LastReply.Embeds.Single().ImageUrl.Should().BeNull("the logo stays small, less prominent than Requestarr's own mark");
        }

        [Test]
        public void user_help_never_mentions_admin_commands()
        {
            HelpCatalog.UserTopics.SelectMany(t => t.Commands).Should().NotContain(c => c.StartsWith("admin"));
            HelpCatalog.UserTopics.SelectMany(t => t.Buttons).Should().NotContain(b => PanelButtons.Admin.Contains(b));
        }
    }
}
