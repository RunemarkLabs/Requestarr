using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Task 4e item 7: what's new as cards. In both channels each version is one line with Read changes; pressing it shows
    // the full card (version and date, For users and For admins, one line per change) only to whoever pressed it.
    [TestFixture]
    public class Item7VersionCardsFixture : WelcomeScenario
    {
        private const string Text = @"# Changelog

## 0.2.0 (2026-10-22)

### For users
- **Follow:** follow a request from its card.
- **Limits:** your panel shows your limits.

### For admins
- **Member cards:** a clearer layout.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private ulong UsersChannel => ulong.Parse(Config.AnnouncementsChannelId);

        [SetUp]
        public async Task Posted()
        {
            Service.ChangelogText = () => Text;
            Service.CurrentVersion = () => new Version(0, 2, 0, 7);
            await Service.PostVersionNotesAsync();
        }

        [Test]
        public void the_admin_card_is_one_line_with_both_counts()
        {
            var card = AdminRoom.Messages.Single(m => m.Embeds.Any());

            card.Content.Should().BeNullOrEmpty();
            card.Embeds.Single().Title.Should().Be("Requestarr 0.2.0 · 1 change for admins, 2 for users");
            card.Embeds.Single().Description.Should().BeNull();
            card.Embeds.Single().Color.Should().Be(VersionCards.AdminsColor);
            card.Buttons.Single().Label.Should().Be(DiscordText.ReadChangesLabel);
            card.Buttons.Single().CustomId.Should().Be("whatsnew:a:0.2.0");
        }

        [Test]
        public void the_announcements_card_counts_the_users_changes_and_names_the_main_one()
        {
            var card = Discord.Channels[UsersChannel].Messages.Single();

            card.Embeds.Single().Title.Should().Be("Requestarr 0.2.0 · 2 new things for you");
            card.Embeds.Single().Description.Should().Be("Follow");
            card.Embeds.Single().Color.Should().Be(VersionCards.UsersColor);
            card.Buttons.Single().CustomId.Should().Be("whatsnew:u:0.2.0");
        }

        [Test]
        public async Task read_changes_shows_the_full_card_privately()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button("whatsnew:a:0.2.0")));

            var full = run.LastReply;
            full.Ephemeral.Should().BeTrue("only the person who pressed it sees it");
            full.Embeds.Should().BeEmpty("cards show text at a message's size");
            full.Cards.Select(c => c.Text).Should().Equal(
                "## Requestarr 0.2.0 · 22 October 2026\n### For users\n- **Follow:** follow a request from its card.\n- **Limits:** your panel shows your limits.",
                "### For admins\n- **Member cards:** a clearer layout.");
            full.Cards.Select(c => c.Color).Should().Equal(VersionCards.UsersColor, VersionCards.AdminsColor);
        }

        [Test]
        public async Task members_read_only_their_section()
        {
            await AddUser(Alice);

            var run = await Harness.SendAsync(FakeInteraction.Button("whatsnew:u:0.2.0").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(UsersChannel));

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Cards.Single().Text.Should().StartWith("## Requestarr 0.2.0").And.Contain("### For users").And.NotContain("Member cards");
        }

        [Test]
        public async Task the_changelog_heading_carries_the_day_the_version_was_made()
        {
            Changelog.Parse(Text).Single().Date.Should().Be(new DateTime(2026, 10, 22));
            Changelog.Parse("## 0.1.0\n\n### For users\n- x\n").Single().Date.Should().BeNull("older headings without a day still parse");

            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button("whatsnew:a:9.9.9")));
            run.LastReply.Content.Should().Be(DiscordText.VersionNotFound(new Version(9, 9, 9)));
        }
    }
}
