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
    [TestFixture]
    public class Item3UpgradeFixture : DiscordScenario
    {
        private const string Text = @"# Changelog

## 0.3.0

### Internal
- Faster startup.

## 0.2.0

### For users
- You can now follow a request from its card.
- Your panel shows your limits.

### For admins
- New member card layout.

### Internal
- Database migration 8.

## 0.1.0

### For users
- Request movies and TV.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private System.Collections.Generic.List<string> AdminPosts => Discord.Channels[AdminChannel].Messages.Select(m => m.Content).Where(c => c.StartsWith("📋")).ToList();

        private System.Collections.Generic.List<string> UserPosts => Discord.Channels[ulong.Parse(Config.AnnouncementsChannelId)].Messages.Select(m => m.Content).ToList();

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

            versions.Select(v => v.Version.ToString()).Should().Equal("0.3.0", "0.2.0", "0.1.0");
            versions[1].Users.Should().Equal("You can now follow a request from its card.", "Your panel shows your limits.");
            versions[1].Admins.Should().Equal("New member card layout.");
            versions[1].Internal.Should().Equal("Database migration 8.");
            versions[0].Users.Should().BeEmpty();
        }

        [Test]
        public async Task the_full_list_goes_to_admins_and_only_user_lines_to_members()
        {
            await StartOn("0.2.0");

            AdminPosts.Single().Should().StartWith("📋 **Requestarr 0.2.0: what's new**")
                .And.Contain("• You can now follow a request from its card.")
                .And.Contain("• New member card layout.")
                .And.Contain("• Database migration 8.")
                .And.Contain($"(posted in #{ChannelLayout.AnnouncementsChannelName})");
            UserPosts.Single().Should().Be("✨ **What's new in Requestarr**\n• You can now follow a request from its card.\n• Your panel shows your limits.");
            Config.AnnouncedVersion.Should().Be("0.2.0");
        }

        [Test]
        public async Task each_version_is_announced_once_even_across_restarts()
        {
            await StartOn("0.2.0");
            await StartOn("0.2.0");
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await StartOn("0.2.0");

            AdminPosts.Should().ContainSingle();
            UserPosts.Should().ContainSingle();
        }

        [Test]
        public async Task a_version_with_nothing_for_users_posts_nothing_to_users()
        {
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().HaveCount(2);
            AdminPosts.Last().Should().StartWith("📋 **Requestarr 0.3.0: what's new**").And.Contain("nothing was posted to users").And.Contain("• Faster startup.");
            UserPosts.Should().ContainSingle("0.3.0 has no user-facing changes");
            Config.AnnouncedVersion.Should().Be("0.3.0");
        }

        [Test]
        public async Task skipped_versions_are_each_announced_in_order()
        {
            Service.ChangelogText = () => Text;
            await StartOn("0.1.0");
            await StartOn("0.3.0");

            AdminPosts.Select(p => p.Split('\n')[0]).Should().Equal("📋 **Requestarr 0.1.0: what's new**", "📋 **Requestarr 0.2.0: what's new**", "📋 **Requestarr 0.3.0: what's new**");
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task the_first_start_with_announcements_posts_only_the_current_version()
        {
            Config.AnnouncedVersion.Should().BeNull("a new server, or one from before announcements");

            await StartOn("0.2.0");

            AdminPosts.Should().ContainSingle().Which.Should().NotContain("Request movies and TV.");
        }

        [Test]
        public async Task an_older_build_never_reposts()
        {
            await StartOn("0.3.0");
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().ContainSingle();
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
        public async Task a_long_list_is_split_into_messages_discord_accepts()
        {
            var lines = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"- Change number {i} with enough words to take some room in the message."));
            Service.ChangelogText = () => $"## 0.9.0\n\n### For admins\n{lines}\n";
            Service.CurrentVersion = () => new Version(0, 9, 0, 1);

            await Service.PostVersionNotesAsync();

            var posts = Discord.Channels[AdminChannel].Messages.Skip(Discord.Channels[AdminChannel].Messages.FindIndex(m => m.Content.StartsWith("📋"))).ToList();
            posts.Should().HaveCountGreaterThan(1);
            posts.Should().OnlyContain(p => p.Content.Length <= DiscordLimits.MessageLength);
            string.Join("\n", posts.Select(p => p.Content)).Should().Contain("Change number 1 ").And.Contain("Change number 60 ");
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
            versions.Should().OnlyContain(v => v.Users.Count + v.Admins.Count + v.Internal.Count > 0, "every version says what changed");
            Changelog.Embedded().Replace("\r", string.Empty).Should().Be(file.Replace("\r", string.Empty), "the build embeds the file");
        }

        [Test]
        public void the_process_and_the_channel_are_documented()
        {
            File.ReadAllText(Item4To6Fixture.RepoFile("docs", "development.md")).Should().Contain("## Changelog").And.Contain("Bump the version").And.Contain("### For users");
            File.ReadAllText(Item4To6Fixture.RepoFile("CLAUDE.md")).Should().Contain("CHANGELOG.md");
            File.ReadAllText(Item4To6Fixture.RepoFile(".github", "pull_request_template.md")).Should().Contain("CHANGELOG.md");
            File.ReadAllText(Item4To6Fixture.RepoFile(".github", "workflows", "build.yml")).Should().Contain("name: Changelog").And.Contain("secrets, changelog]");
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
