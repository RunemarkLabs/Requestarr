using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Announcements
{
    // Task 4d item 7: the "what's new" posts are a standing record, kept in step with each environment. Production and
    // staging keep every version for good (Item3UpgradeFixture); QA shows what staging doesn't have yet and rebuilds
    // after a reset; dev shows only the version running now. Task 4e: each post is a one-line card; the lists below are
    // the versions of the cards in each channel, oldest first.
    [TestFixture]
    public class Item4dVersionPostsFixture : DiscordScenario
    {
        private const string Text = @"# Changelog

## 0.3.0

### For admins
- **Faster start:** Requestarr is ready sooner after a restart.

## 0.2.5

## 0.2.0

### For users
- **Follow:** follow a request from its card.

### For admins
- **Member cards:** a clearer layout.

## 0.1.0

### For users
- **Requests:** ask for movies and TV.
";

        // A dev or QA build's changelog: what changed since the last build sent to testers, above the numbered versions.
        private const string Pending = @"# Changelog

## Unreleased

### For admins
- **Build numbers:** each build sent to testers has one.

## 0.6.0 (2026-11-13)

### For admins
- **Cards:** clearer.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private ulong UsersChannel => ulong.Parse(Config.AnnouncementsChannelId);

        private List<string> AdminHeaders => Cards(AdminChannel, VersionAudience.Admins);

        private List<string> UserHeaders => Cards(UsersChannel, VersionAudience.Users);

        private List<string> Cards(ulong channel, VersionAudience audience) =>
            Discord.Channels[channel].Messages.Where(m => m.Embeds.Any()).Select(m => VersionCards.Parse(m.Buttons.Select(b => b.CustomId), audience)?.ToString()).Where(v => v != null).ToList();

        private void Environment(string name, string staged = null)
        {
            var environment = new ReleaseEnvironment
            {
                Variable = v => v switch
                {
                    ReleaseEnvironment.EnvironmentVariable => name,
                    ReleaseEnvironment.StagedVersionsVariable => staged,
                    _ => null
                }
            };
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(environment.VersionPosts);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.Staged).Returns(environment.Staged);
        }

        private async Task StartOn(string version, string text = Text)
        {
            Service.ChangelogText = () => text;
            Service.CurrentVersion = () => new Version(version + ".42");
            await Service.PostVersionNotesAsync();
        }

        private static string Admin(string version) => version;

        private static string UserPost(string version) => version;

        [Test]
        public void the_environment_decides_how_posts_are_kept()
        {
            VersionPostMode Mode(string name) => new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? name : null }.VersionPosts;

            (Mode("dev"), Mode("qa"), Mode(" QA "), Mode("staging"), Mode(null), Mode("anything else"))
                .Should().Be((VersionPostMode.CurrentOnly, VersionPostMode.NotYetStaged, VersionPostMode.NotYetStaged, VersionPostMode.Standing, VersionPostMode.Standing, VersionPostMode.Standing));
            new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.StagedVersionsVariable ? "0.1.0, 0.2.0,junk" : null }.Staged
                .Should().BeEquivalentTo(new[] { new Version(0, 1, 0), new Version(0, 2, 0) });
            new ReleaseEnvironment { Variable = _ => null }.Staged.Should().BeEmpty();
        }

        [Test]
        public async Task qa_shows_every_version_staging_does_not_have_yet()
        {
            Environment("qa", "0.1.0");

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.2.0"), Admin("0.3.0"));
            UserHeaders.Should().Equal(UserPost("0.2.0"));
        }

        [Test]
        public async Task with_nothing_in_staging_qa_shows_the_full_history()
        {
            Environment("qa");

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.1.0"), Admin("0.2.0"), Admin("0.3.0"));
            UserHeaders.Should().Equal(UserPost("0.1.0"), UserPost("0.2.0"));
        }

        [Test]
        public async Task versions_promoted_to_staging_drop_off_qa()
        {
            Environment("qa");
            await StartOn("0.3.0");

            Environment("qa", "0.1.0,0.2.0,0.2.5");
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.3.0"));
            UserHeaders.Should().BeEmpty();

            Environment("qa", "0.1.0,0.2.0,0.2.5,0.3.0");
            await StartOn("0.3.0");

            AdminHeaders.Should().BeEmpty("everything in QA is in staging");
        }

        [Test]
        public async Task a_qa_reset_rebuilds_the_list_rather_than_wiping_it()
        {
            Environment("qa", "0.1.0");
            await StartOn("0.3.0");

            foreach (var channel in new[] { AdminChannel, UsersChannel })
            {
                foreach (var id in Discord.Channels[channel].MessageIds.ToList())
                {
                    Discord.DeleteMessage(channel, id);
                }
            }

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.2.0"), Admin("0.3.0"));
            UserHeaders.Should().Equal(UserPost("0.2.0"));
        }

        [Test]
        public async Task restarting_changes_nothing_and_new_versions_are_added_in_order()
        {
            Environment("qa");
            await StartOn("0.2.0");
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();

            await StartOn("0.2.0");
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids, "nothing reposted");

            await StartOn("0.3.0");
            AdminHeaders.Should().Equal(Admin("0.1.0"), Admin("0.2.0"), Admin("0.3.0"));
            Discord.Channels[AdminChannel].MessageIds.Take(ids.Count).Should().Equal(ids);
        }

        [Test]
        public async Task a_version_that_reaches_qa_late_is_put_back_in_order()
        {
            Environment("qa", "0.1.0,0.2.0");
            await StartOn("0.3.0");
            Environment("qa", "0.1.0");

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.2.0"), Admin("0.3.0"));
        }

        [Test]
        public async Task rewritten_words_are_edited_in_place()
        {
            Environment("qa");
            await StartOn("0.3.0");
            var ids = Discord.Channels[UsersChannel].MessageIds.ToList();
            Discord.Channels[UsersChannel].Messages.First().Embeds[0].Description.Should().Be("Requests", "the main change in a few words");

            await StartOn("0.3.0", Text.Replace("**Requests:**", "**Requesting:**"));

            Discord.Channels[UsersChannel].MessageIds.Should().Equal(ids);
            Discord.Channels[UsersChannel].Messages.First().Embeds[0].Description.Should().Be("Requesting");
        }

        [Test]
        public async Task old_style_posts_are_replaced_and_other_messages_left_alone()
        {
            Environment("qa");
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "⚠️ Sonarr (test) can't be reached." });
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "📋 **Requestarr 0.1.0: what's new**\n**For users:** nothing this time, so nothing was posted to users.\n**Internal**\n• x" });
            await Discord.SendMessageAsync(UsersChannel, new InteractionReply { Content = "✨ **What's new in Requestarr**\n• Old." });

            await StartOn("0.3.0");

            Discord.Channels[AdminChannel].Messages.Select(m => m.Content).Should().Contain("⚠️ Sonarr (test) can't be reached.");
            AdminHeaders.Should().Equal(Admin("0.1.0"), Admin("0.2.0"), Admin("0.3.0"));
            string.Join("\n", Discord.Channels[AdminChannel].Messages.Select(m => m.Content ?? string.Empty)).Should().NotContain("nothing this time").And.NotContain("Internal");
            UserHeaders.Should().Equal(UserPost("0.1.0"), UserPost("0.2.0"));
        }

        [Test]
        public async Task dev_shows_only_the_version_running_now()
        {
            Environment("dev");

            await StartOn("0.2.0");
            AdminHeaders.Should().Equal(Admin("0.2.0"));
            UserHeaders.Should().Equal(UserPost("0.2.0"));

            await StartOn("0.3.0");
            AdminHeaders.Should().Equal(Admin("0.3.0"));
            UserHeaders.Should().BeEmpty("0.3.0 has nothing for users");

            await StartOn("0.2.5");
            AdminHeaders.Should().BeEmpty("a version with nothing for either posts nothing");
        }

        [Test]
        public async Task staging_never_removes_a_card()
        {
            Environment("staging");
            await Discord.SendMessageAsync(AdminChannel, OldCard("0.0.9", "a", "Requestarr 0.0.9 · 1 change for admins"));

            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.0.9"), Admin("0.1.0"), Admin("0.2.0"), Admin("0.3.0"));
        }

        [Test]
        public void a_version_shows_as_the_build_label_only_for_the_version_the_build_runs()
        {
            string Shown(string label, string version) =>
                new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.ReleaseVersionVariable ? label : null }.Shown(new Version(version));

            Shown("0.7.0004-dev.4f2a9c1", "0.7.4.11354").Should().Be("0.7.0004-dev.4f2a9c1");
            Shown(" 0.7.0004-qa.4f2a9c1 ", "0.7.4").Should().Be("0.7.0004-qa.4f2a9c1");
            Shown("0.7.0004", "0.7.4").Should().Be("0.7.0004", "a build sent to testers shows its number");
            Shown("0.7.0004-dev.4f2a9c1", "0.6.0").Should().Be("0.6.0", "an older version keeps its own number");
            Shown(null, "0.7.12").Should().Be("0.7.0012", "a build without a label shows its number, four digits from 0.7 on");
            Shown("0.7.0004-beta.1", "0.7.4").Should().Be("0.7.0004", "only the labels Requestarr's builds use count");
            Shown("0.7.4-dev.4f2a9c1", "0.7.4").Should().Be("0.7.0004", "a label without the four digits isn't one");
        }

        [Test]
        public async Task dev_shows_unreleased_as_the_build_it_runs_under_its_label()
        {
            Environment("dev");
            var environment = new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.ReleaseVersionVariable ? "0.7.0004-dev.4f2a9c1" : null };
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns<Version>(environment.Shown);

            await StartOn("0.7.4", Pending);

            AdminHeaders.Should().Equal("0.7.4");
            Discord.Channels[AdminChannel].Messages.Single(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004-dev.4f2a9c1", "Unreleased has no day yet");
            Service.FullCard(VersionAudience.Admins, new Version(0, 7, 4))[0].Cards[0].Text.Should().StartWith("## Requestarr 0.7.0004-dev.4f2a9c1\n");
        }

        [Test]
        public async Task qa_shows_unreleased_with_what_staging_does_not_have_yet()
        {
            Environment("qa", "0.6.0");

            await StartOn("0.7.4", Pending);

            AdminHeaders.Should().Equal("0.7.4");
        }

        [Test]
        public async Task qa_shows_unreleased_as_everything_since_the_last_beta_and_a_folded_version_loses_its_card()
        {
            Environment("qa", "0.5.6");
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns<Version>(v => v == new Version(0, 7, 4) ? "0.7.0004-qa.4f2a9c1" : Changelog.Display(v));
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "something else in the channel" });
            await StartOn("0.7.4", Pending + "\n## 0.5.6 (2026-11-13)\n\n### For admins\n- **Testers have it:** yes.\n");
            AdminHeaders.Should().Equal("0.6.0", "0.7.4");

            // 0.6.0 never reached the testers: it's folded into Unreleased.
            await StartOn("0.7.4", Pending.Replace("## 0.6.0 (2026-11-13)", "## 0.5.6 (2026-11-13)"));

            AdminHeaders.Should().Equal("0.7.4");
            Discord.Channels[AdminChannel].Messages.Single(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004-qa.4f2a9c1 · since the last beta");
            Service.FullCard(VersionAudience.Admins, new Version(0, 7, 4))[0].Cards[0].Text.Should().StartWith("## Requestarr 0.7.0004-qa.4f2a9c1 · since the last beta\n");
            Discord.Channels[AdminChannel].Messages.Should().Contain(m => m.Content == "something else in the channel", "only version cards go");
        }

        [Test]
        public async Task staging_shows_a_build_by_its_four_digit_number_and_never_unreleased()
        {
            Environment("staging");
            var numbered = Pending.Replace("## Unreleased", $"## 0.7.0004 ({DateTime.UtcNow.Year}-11-20)");

            await StartOn("0.6.0", Pending);
            AdminHeaders.Should().Equal("0.6.0");

            await StartOn("0.7.4", numbered);
            AdminHeaders.Should().Equal("0.6.0", "0.7.4");
            Discord.Channels[AdminChannel].Messages.Last(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004 · November 20");
        }

        [Test]
        public void a_card_is_found_by_its_read_changes_button_whatever_its_title()
        {
            VersionCards.Parse(new[] { "whatsnew:a:0.4.17" }, VersionAudience.Admins).Should().Be(new Version(0, 4, 17));
            VersionCards.Parse(new[] { "whatsnew:u:0.4.17" }, VersionAudience.Users).Should().Be(new Version(0, 4, 17));
            VersionCards.Parse(new[] { "whatsnew:u:0.4.17" }, VersionAudience.Admins).Should().BeNull("the other channel's card");
            VersionCards.Parse(new[] { "whatsnew:a:soon" }, VersionAudience.Admins).Should().BeNull();
            VersionCards.Parse(new[] { "admin:panel" }, VersionAudience.Admins).Should().BeNull();
            VersionCards.Parse(null, VersionAudience.Admins).Should().BeNull("a message without buttons");
        }

        // A card as an earlier version of Requestarr posted it: the same button, an older title.
        private static InteractionReply OldCard(string version, string audience, string title) => new()
        {
            Embeds = new List<InteractionEmbed> { new() { Title = title } },
            Buttons = new List<InteractionButton> { new() { CustomId = $"{VersionCardRoutes.ReadChanges}:{audience}:{version}", Label = DiscordText.ReadChangesLabel } }
        };

        [TestCase("staging")]
        [TestCase("production")]
        [TestCase("qa")]
        [TestCase("dev")]
        public async Task cards_with_the_old_titles_are_updated_in_place_on_the_next_start(string environment)
        {
            Environment(environment);
            await StartOn("0.2.0");
            var admin = Discord.Channels[AdminChannel];
            var users = Discord.Channels[UsersChannel];
            var adminIds = admin.MessageIds.ToList();
            var userIds = users.MessageIds.ToList();
            var wanted = admin.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).ToList();
            var wantedUsers = users.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).ToList();

            // What the server shows after its last start on an earlier version of Requestarr.
            foreach (var (id, message) in admin.MessageIds.Zip(admin.Messages).ToList())
            {
                var version = VersionCards.Parse(message.Buttons.Select(b => b.CustomId), VersionAudience.Admins);
                await Discord.EditMessageAsync(AdminChannel, id, OldCard(version.ToString(), "a", $"Requestarr {version} · 1 change for admins"));
            }

            foreach (var (id, message) in users.MessageIds.Zip(users.Messages).ToList())
            {
                var version = VersionCards.Parse(message.Buttons.Select(b => b.CustomId), VersionAudience.Users);
                await Discord.EditMessageAsync(UsersChannel, id, OldCard(version.ToString(), "u", $"Requestarr {version} · 1 new thing for you"));
            }

            await StartOn("0.2.0");

            admin.MessageIds.Should().Equal(adminIds, "the cards are edited, not posted again, so the record and order stay");
            users.MessageIds.Should().Equal(userIds);
            admin.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).Should().Equal(wanted);
            users.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).Should().Equal(wantedUsers);
            admin.Messages.Should().OnlyContain(m => !m.Embeds[0].Title.Contains("change"));
        }

        [Test]
        public async Task staging_replaces_its_posts_from_before_cards_once_keeping_the_history()
        {
            Environment("staging");
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "📋 **Requestarr 0.1.0: what's new**\n**For users**\n• Old." });
            await Discord.SendMessageAsync(UsersChannel, new InteractionReply { Content = "✨ **What's new in Requestarr 0.1.0**\n• Old." });
            var config = Config;
            config.AnnouncedVersion = "0.2.0";
            Mocker.Resolve<Requestarr.Core.Discord.IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.2.0");
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();
            await StartOn("0.2.0");

            AdminHeaders.Should().Equal(Admin("0.1.0"), Admin("0.2.0"));
            UserHeaders.Should().Equal(UserPost("0.1.0"), UserPost("0.2.0"));
            Discord.Channels[AdminChannel].Messages.Should().NotContain(m => m.Content != null && m.Content.StartsWith("📋"));
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids, "converted once");
        }

        [Test]
        public async Task a_long_version_is_kept_as_one_card_across_restarts()
        {
            Environment("qa");
            var lines = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"- **Change {i}:** with enough words to take some room in the message."));
            var text = $"## 0.9.0\n\n### For admins\n{lines}\n";
            await StartOn("0.9.0", text);
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();

            await StartOn("0.9.0", text);

            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids);
            AdminHeaders.Should().ContainSingle("a long version is still one card").Which.Should().Be(Admin("0.9.0"));
        }
    }
}
