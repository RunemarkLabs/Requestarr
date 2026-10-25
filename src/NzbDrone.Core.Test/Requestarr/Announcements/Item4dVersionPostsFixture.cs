using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
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

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private ulong UsersChannel => ulong.Parse(Config.AnnouncementsChannelId);

        private List<string> AdminHeaders => Cards(AdminChannel, VersionAudience.Admins);

        private List<string> UserHeaders => Cards(UsersChannel, VersionAudience.Users);

        private List<string> Cards(ulong channel, VersionAudience audience) =>
            Discord.Channels[channel].Messages.Where(m => m.Embeds.Any()).Select(m => VersionCards.Parse(m.Embeds[0].Title, audience)?.ToString()).Where(v => v != null).ToList();

        private void Environment(string name, string staged = null)
        {
            var environment = new ReleaseEnvironment { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? name : v == ReleaseEnvironment.StagedVersionsVariable ? staged : null };
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
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Embeds = new List<InteractionEmbed> { new() { Title = "Requestarr 0.0.9 · 1 change for admins" } } });

            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(Admin("0.0.9"), Admin("0.1.0"), Admin("0.2.0"), Admin("0.3.0"));
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
