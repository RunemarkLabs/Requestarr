using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.AnimeDub;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Announcements
{
    // Task 3d item 3: on a new version, the full list to the admin channel, the user-facing lines to the
    // announcements channel (nothing to users when there are none), once per version, across restarts.
    // Task 4d: two sections only, empty ones never shown; production and staging (the default here) keep every post
    // and start with the full history. Task 4e: each post is a one-line card (Read changes shows the full card).
    [TestFixture]
    public class Item3UpgradeFixture : DiscordScenario
    {
        private const string Text = @"# Changelog

## 0.3.0

### For admins
- Faster startup.

## 0.2.5

## 0.2.0

### For users
- You can now follow a request from its card.
- Your panel shows your limits.

### For admins
- New member card layout.

## 0.1.0

### For users
- Request movies and TV.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        // The version cards' titles, oldest first.
        private System.Collections.Generic.List<string> AdminPosts => Titles(AdminChannel);

        private System.Collections.Generic.List<string> UserPosts => Titles(ulong.Parse(Config.AnnouncementsChannelId));

        private System.Collections.Generic.List<string> Titles(ulong channel) =>
            Discord.Channels[channel].Messages.Where(m => m.Embeds.Any()).Select(m => m.Embeds[0].Title).Where(t => t != null && t.StartsWith("Requestarr ")).ToList();

        private static string Admin(string version, string counts) => $"Requestarr {version} · {counts}";

        private static string UserCard(string version, int count) => $"Requestarr {version} · {count} new thing{(count == 1 ? string.Empty : "s")} for you";

        private async Task StartOn(string version)
        {
            Service.ChangelogText = () => Text;
            Service.CurrentVersion = () => new Version(version + ".123");
            await Service.PostVersionNotesAsync();
        }

        [Test]
        public void the_changelog_parses_into_versions_and_sections()
        {
            var versions = Changelog.Parse(Text);

            versions.Select(v => v.Version.ToString()).Should().Equal("0.3.0", "0.2.5", "0.2.0", "0.1.0");
            versions[2].Users.Should().Equal("You can now follow a request from its card.", "Your panel shows your limits.");
            versions[2].Admins.Should().Equal("New member card layout.");
            versions[0].Users.Should().BeEmpty();
            versions[1].HasNews.Should().BeFalse("a version with nothing for users or admins has only its heading");
        }

        [Test]
        public async Task the_full_list_goes_to_admins_and_only_user_lines_to_members()
        {
            await StartOn("0.2.0");

            AdminPosts.Last().Should().Be(Admin("0.2.0", "1 change for admins, 2 for users"));
            UserPosts.Last().Should().Be(UserCard("0.2.0", 2));

            var full = Service.FullCard(VersionAudience.Admins, new Version(0, 2, 0)).Single();
            full.Ephemeral.Should().BeTrue();
            full.Embeds.Select(e => e.Description).Should().Equal(
                "**For users**\n• You can now follow a request from its card.\n• Your panel shows your limits.",
                "**For admins**\n• New member card layout.");
            Service.FullCard(VersionAudience.Users, new Version(0, 2, 0)).Single().Embeds.Should().ContainSingle("members see only their lines");
            Config.AnnouncedVersion.Should().Be("0.1.0,0.2.0");
        }

        [Test]
        public async Task each_version_is_announced_once_even_across_restarts()
        {
            await StartOn("0.2.0");
            await StartOn("0.2.0");
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await StartOn("0.2.0");

            AdminPosts.Should().HaveCount(2, "0.1.0 and 0.2.0, once each");
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task a_version_with_nothing_for_users_posts_nothing_to_users_and_no_users_section()
        {
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().HaveCount(3);
            AdminPosts.Last().Should().Be(Admin("0.3.0", "1 change for admins"));
            UserPosts.Should().HaveCount(2, "0.3.0 has nothing for users");
            Config.AnnouncedVersion.Should().Be("0.1.0,0.2.0,0.2.5,0.3.0");
        }

        [Test]
        public async Task a_version_with_nothing_for_anyone_posts_nothing_anywhere()
        {
            await StartOn("0.2.0");
            var admin = AdminPosts.Count;
            var users = UserPosts.Count;

            await StartOn("0.2.5");

            (AdminPosts.Count, UserPosts.Count).Should().Be((admin, users));
            Config.AnnouncedVersion.Should().Contain("0.2.5", "recorded, so it never posts later");
            string.Join("\n", AdminPosts.Concat(UserPosts)).Should().NotContain("nothing this time").And.NotContain("Internal");
        }

        [Test]
        public async Task skipped_versions_are_each_announced_in_order()
        {
            Service.ChangelogText = () => Text;
            await StartOn("0.1.0");
            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Admin("0.1.0", "1 change for users"), Admin("0.2.0", "1 change for admins, 2 for users"), Admin("0.3.0", "1 change for admins"));
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task the_first_start_posts_the_full_history_oldest_first()
        {
            Config.AnnouncedVersion.Should().BeNull("a new server");

            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Admin("0.1.0", "1 change for users"), Admin("0.2.0", "1 change for admins, 2 for users"), Admin("0.3.0", "1 change for admins"));
            UserPosts.Should().Equal(UserCard("0.1.0", 1), UserCard("0.2.0", 2));
        }

        [Test]
        public async Task a_server_set_up_again_with_its_channels_kept_does_not_repeat_the_history()
        {
            await StartOn("0.2.0");
            var config = Config;
            config.AnnouncedVersion = null;
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Admin("0.1.0", "1 change for users"), Admin("0.2.0", "1 change for admins, 2 for users"), Admin("0.3.0", "1 change for admins"));
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task an_older_build_never_reposts()
        {
            await StartOn("0.3.0");
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().HaveCount(3, "0.1.0, 0.2.0 and 0.3.0, once each");
        }

        // Task 4c: a fix made in QA takes the next number above dev's, so the dev versions it skipped reach QA later.
        [Test]
        public async Task versions_a_qa_fix_skipped_over_are_announced_when_they_arrive()
        {
            const string qa = @"## 0.2.6

### For users
- The fix.

## 0.2.3

### For admins
- Promoted.
";
            const string promoted = @"## 0.2.7

### For admins
- Newer.

## 0.2.6

### For users
- The fix.

## 0.2.5

### For users
- Five.

## 0.2.4

### For admins
- Four.

## 0.2.3

### For admins
- Promoted.
";

            Service.ChangelogText = () => qa;
            Service.CurrentVersion = () => new Version(0, 2, 3, 1);
            await Service.PostVersionNotesAsync();
            Service.CurrentVersion = () => new Version(0, 2, 6, 1);
            await Service.PostVersionNotesAsync();
            Service.ChangelogText = () => promoted;
            Service.CurrentVersion = () => new Version(0, 2, 7, 1);
            await Service.PostVersionNotesAsync();
            await Service.PostVersionNotesAsync();

            AdminPosts.Select(t => t.Split(' ')[1]).Should().Equal("0.2.3", "0.2.6", "0.2.4", "0.2.5", "0.2.7");
            UserPosts.Should().Equal(UserCard("0.2.6", 1), UserCard("0.2.5", 1));
            Config.AnnouncedVersion.Should().Be("0.2.3,0.2.4,0.2.5,0.2.6,0.2.7");
        }

        [Test]
        public async Task a_record_from_before_the_list_counts_everything_up_to_it_as_announced()
        {
            var config = Config;
            config.AnnouncedVersion = "0.2.0";
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.3.0");

            AdminPosts.Should().ContainSingle().Which.Should().StartWith("Requestarr 0.3.0 · ");
            Config.AnnouncedVersion.Should().Be("0.2.0,0.2.5,0.3.0");
        }

        [Test]
        public void the_announced_list_reads_and_writes_versions()
        {
            var announced = Announced.Parse("0.2.3, 0.2.6,junk");

            announced.Contains(new Version(0, 1, 0)).Should().BeTrue("everything up to the lowest entry");
            announced.Contains(new Version(0, 2, 6)).Should().BeTrue();
            announced.Contains(new Version(0, 2, 4)).Should().BeFalse("skipped by the fix");
            announced.With(new[] { new Version(0, 2, 4) }).ToString().Should().Be("0.2.3,0.2.4,0.2.6");
            Announced.Parse(null).IsEmpty.Should().BeTrue();
        }

        [Test]
        public async Task nothing_is_marked_announced_when_discord_refuses_the_post()
        {
            Discord.Channels.Remove(AdminChannel);

            await StartOn("0.2.0");

            Config.AnnouncedVersion.Should().BeNull("tried again on the next start");
            NzbDrone.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_long_version_is_one_card_and_its_full_card_splits_within_discord_limits()
        {
            var lines = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"- Change number {i} with enough words to take some room in the message."));
            Service.ChangelogText = () => $"## 0.9.0 (2026-10-25)\n\n### For users\n{lines}\n\n### For admins\n{lines}\n";
            Service.CurrentVersion = () => new Version(0, 9, 0, 1);

            await Service.PostVersionNotesAsync();

            AdminPosts.Should().Equal(Admin("0.9.0", "200 changes for admins, 200 for users"));
            var full = Service.FullCard(VersionAudience.Admins, new Version(0, 9, 0));
            full.Should().HaveCountGreaterThan(1, "more than 6,000 characters takes more than one message");
            full[0].Embeds[0].Title.Should().Be("Requestarr 0.9.0 · 25 October 2026");

            foreach (var reply in full)
            {
                reply.Embeds.Count.Should().BeLessOrEqualTo(VersionCards.EmbedsPerMessage);
                reply.Embeds.Sum(e => (e.Title?.Length ?? 0) + e.Description.Length).Should().BeLessOrEqualTo(VersionCards.MessageEmbedsLength);
                reply.Embeds.Should().OnlyContain(e => e.Description.Length <= VersionCards.EmbedTextLength);
            }

            var text = full.SelectMany(r => r.Embeds).Select(e => e.Description).ToList();
            text.SelectMany(t => t.Split('\n')).Where(l => l.StartsWith("• ")).Should().HaveCount(400, "every line, none cut");
            text.Count(t => t.StartsWith("**For users** (continued)")).Should().BeGreaterThan(0);
        }

        // The shipped CHANGELOG.md: the process every pull request follows (docs/development.md).
        [Test]
        public void the_shipped_changelog_matches_the_build_version_and_is_embedded()
        {
            var file = File.ReadAllText(Item4To6Fixture.RepoFile("CHANGELOG.md"));
            var props = File.ReadAllText(Item4To6Fixture.RepoFile("src", "Directory.Build.props"));
            var assembly = Regex.Match(props, @"<AssemblyVersion>(\d+\.\d+\.\d+)").Groups[1].Value;
            var versions = Changelog.Parse(file);

            versions.Should().NotBeEmpty();
            versions[0].Version.ToString().Should().Be(assembly, "the top entry is the version this build announces");
            versions.Select(v => v.Version).Should().OnlyHaveUniqueItems();
            versions.Should().Contain(v => v.HasNews);
            Changelog.Embedded().Replace("\r", string.Empty).Should().Be(file.Replace("\r", string.Empty), "the build embeds the file");
        }

        [Test]
        public void the_process_and_the_channel_are_documented()
        {
            File.ReadAllText(Item4To6Fixture.RepoFile("docs", "development.md")).Should().Contain("## Changelog").And.Contain("Bump the version").And.Contain("### For users");
            File.ReadAllText(Item4To6Fixture.RepoFile("CLAUDE.md")).Should().Contain("CHANGELOG.md");
            File.ReadAllText(Item4To6Fixture.RepoFile(".github", "pull_request_template.md")).Should().Contain("CHANGELOG.md");
            File.ReadAllText(Item4To6Fixture.RepoFile(".github", "workflows", "build.yml")).Should().Contain("name: Changelog").And.Contain("secrets, changelog, promotion]");
            File.ReadAllText(Item4To6Fixture.RepoFile("docs", "setup-guide.md")).Should().Contain("### 5.8 The announcements channel").And.Contain("**Announce to users**").And.Contain("Requestarr Members");
            File.ReadAllText(Item4To6Fixture.RepoFile("docs", "commands.md")).Should().Contain("### Announcements");
        }

        [Test]
        public void user_lines_are_in_plain_words()
        {
            var technical = new[] { "Sonarr", "Radarr", "Lidarr", "API", "webhook", "database", "migration", ".NET", "refactor", "provisioning", "endpoint", "CI" };

            foreach (var line in Changelog.Parse(File.ReadAllText(Item4To6Fixture.RepoFile("CHANGELOG.md"))).SelectMany(v => v.Users))
            {
                technical.Where(word => Regex.IsMatch(line, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)).Should().BeEmpty($"\"{line}\" is for users");
            }
        }
    }
}
