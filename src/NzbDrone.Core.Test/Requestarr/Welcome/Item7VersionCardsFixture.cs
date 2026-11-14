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
    // Task 4e item 7: what's new as cards. In both channels each version is one line (since 0.6.0 its version and date,
    // then its main change and how many more) with Read changes; pressing it shows the full card (version and date,
    // For users and For admins, one line per change) only to whoever pressed it.
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

        // The card's year shows only when the version is from another year than the one the server runs in.
        private static string Dated(string title) => title + (DateTime.UtcNow.Year == 2026 ? string.Empty : ", 2026");

        [Test]
        public void the_admin_card_is_the_version_and_its_date_then_its_main_change_and_how_many_more()
        {
            var card = AdminRoom.Messages.Single(m => m.Embeds.Any());

            card.Content.Should().BeNullOrEmpty();
            card.Embeds.Single().Title.Should().Be(Dated("Requestarr 0.2.0 · October 22"));
            card.Embeds.Single().Description.Should().Be("Member cards, and 2 more", "the admins' own change leads, then every other line of the card");
            card.Embeds.Single().Color.Should().Be(VersionCards.AdminsColor);
            card.Buttons.Single().Label.Should().Be(DiscordText.ReadChangesLabel);
            card.Buttons.Single().CustomId.Should().Be("whatsnew:a:0.2.0");
        }

        [Test]
        public void the_announcements_card_is_the_version_and_its_date_then_the_main_change_and_how_many_more()
        {
            var card = Discord.Channels[UsersChannel].Messages.Single();

            card.Embeds.Single().Title.Should().Be(Dated("Requestarr 0.2.0 · October 22"));
            card.Embeds.Single().Description.Should().Be("Follow, and 1 more");
            card.Embeds.Single().Color.Should().Be(VersionCards.UsersColor);
            card.Buttons.Single().CustomId.Should().Be("whatsnew:u:0.2.0");
        }

        [TestCase(2026, "Requestarr 0.4.11 · October 25")]
        [TestCase(2027, "Requestarr 0.4.11 · October 25, 2026")]
        public void the_date_shows_its_year_only_when_it_is_not_this_year(int thisYear, string title)
        {
            var version = new ChangelogVersion(new Version(0, 4, 11), new[] { "**Softer logo:** easier on the eyes." }, Array.Empty<string>(), new DateTime(2026, 10, 25));

            VersionCards.Collapsed(version, VersionAudience.Users, thisYear).Title.Should().Be(title);
        }

        [Test]
        public void a_card_with_one_change_names_it_alone_and_a_build_keeps_its_label()
        {
            var version = new ChangelogVersion(new Version(0, 7, 4), new[] { "**Softer logo:** easier on the eyes." }, Array.Empty<string>(), new DateTime(2026, 11, 14));

            var card = VersionCards.Collapsed(version, VersionAudience.Users, 2026, "0.7.0004-qa.4f2a9c1");

            card.Title.Should().Be("Requestarr 0.7.0004-qa.4f2a9c1 · November 14");
            VersionCards.Collapsed(version, VersionAudience.Users, 2026).Title.Should().Be("Requestarr 0.7.0004 · November 14", "a build number shows four digits");
            card.Description.Should().Be("Softer logo");
            VersionCards.Collapsed(version, VersionAudience.Admins, 2026).Description.Should().Be("Softer logo", "the admin card covers the users' lines too");
        }

        [Test]
        public void a_version_without_a_date_shows_only_its_number()
        {
            var version = new ChangelogVersion(new Version(0, 1, 0), new[] { "Request movies and TV." }, Array.Empty<string>());

            VersionCards.Collapsed(version, VersionAudience.Users, 2026).Title.Should().Be("Requestarr 0.1.0");
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
        public async Task pressing_read_changes_again_replaces_the_earlier_copy()
        {
            var first = await Harness.SendAsync(AsAdmin(FakeInteraction.Button("whatsnew:a:0.2.0")));
            var second = await Harness.SendAsync(AsAdmin(FakeInteraction.Button("whatsnew:a:0.2.0")));

            first.Responder.PrivateRepliesDeleted.Should().BeTrue();
            second.Responder.PrivateRepliesDeleted.Should().BeFalse();
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
