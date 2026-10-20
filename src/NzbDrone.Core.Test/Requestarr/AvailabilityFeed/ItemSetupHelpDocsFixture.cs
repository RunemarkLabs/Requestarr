using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.AnimeDub;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.AvailabilityFeed
{
    // Task 3 setup, help and docs: the availability provisioning block, webhook tokens, help topics
    // and the setup guide / command reference.
    [TestFixture]
    public class ItemSetupHelpDocsFixture : FeedScenario
    {
        [TestCase("http://requestarr:9797", null, true)]
        [TestCase("https://requests.example/requestarr", 30, true)]
        [TestCase("requestarr:9797", null, false)]
        [TestCase("ftp://requestarr", null, false)]
        [TestCase("http://requestarr:9797?token=x", null, false)]
        [TestCase(null, 4, false)]
        public void the_availability_block_is_checked(string url, int? minutes, bool valid)
        {
            var errors = ProvisioningLoader.Validate(new ProvisioningDocument { Availability = new AvailabilityProvisioning { WebhookUrl = url, PollMinutes = minutes } }).ToList();

            errors.Any(e => e.StartsWith("availability.")).Should().Be(!valid);
        }

        [Test]
        public void the_poll_interval_defaults_to_15_minutes_and_never_goes_below_5()
        {
            var store = new AvailabilitySettingsStore();
            store.Set(" http://requestarr:9797/ ", null);
            store.WebhookUrl.Should().Be("http://requestarr:9797");
            store.PollMinutes.Should().Be(15);

            store.Set(null, 1);
            store.WebhookUrl.Should().BeNull("no address: polling only");
            store.PollMinutes.Should().Be(5);
        }

        [Test]
        public void each_instance_gets_its_own_token_and_a_new_api_key_changes_them_all()
        {
            var key = string.Concat(Enumerable.Repeat("cd", 16));
            var other = string.Concat(Enumerable.Repeat("ef", 16));

            WebhookTokens.For(key, 1).Should().HaveLength(32).And.NotBe(WebhookTokens.For(key, 2));
            WebhookTokens.For(other, 1).Should().NotBe(WebhookTokens.For(key, 1));
            WebhookTokens.Matches(key, 1, WebhookTokens.For(key, 1)).Should().BeTrue();
            WebhookTokens.Matches(key, 1, WebhookTokens.For(key, 2)).Should().BeFalse();
            WebhookTokens.Url("http://requestarr:9797/", key, 3).Should().Be($"http://requestarr:9797/api/v1/webhook/3?token={WebhookTokens.For(key, 3)}");
        }

        [Test]
        public async Task users_get_the_downloads_topic_and_the_seasons_topic_explains_adding()
        {
            var help = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "downloads").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            help.LastReply.Content.Should().StartWith("**Downloads and availability**").And.Contain("every episode that has aired");

            await SetAccess(Alice, "tv", "approval");
            var seasons = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "seasons").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            seasons.LastReply.Content.Should().Contain(DiscordText.AddSeasonsLabel).And.Contain("never removed");
        }

        [Test]
        public async Task admins_get_the_feed_and_downloads_topics()
        {
            var feed = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "feed").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            feed.LastReply.Content.Should().StartWith("**The admin feed**").And.Contain(ChannelLayout.FeedChannelName).And.Contain("never deleted");

            var downloads = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "downloads").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            downloads.LastReply.Content.Should().Contain("availability.webhookUrl").And.Contain(Requestarr.Core.Backends.ArrApiBase.WebhookName).And.Contain("pollingOnly");

            HelpCatalog.AdminTopics.Select(t => t.Key).Should().Contain(new[] { "feed", "downloads" });
            HelpCatalog.UserTopics.Concat(HelpCatalog.AdminTopics).Should().OnlyContain(t => t.Intro(HelpViewer.Everything).Length < DiscordLimits.MessageLength - 200);
        }

        [Test]
        public void the_setup_guide_explains_webhook_reachability_and_the_feed()
        {
            var guide = File.ReadAllText(Item4To6Fixture.RepoFile("docs", "setup-guide.md"));

            guide.Should().Contain("### 4.8 Downloads and availability")
                .And.Contain("webhookUrl: http://requestarr:9797")
                .And.Contain("Docker, Sonarr/Radarr in the same Compose file")
                .And.Contain("Not in Docker, all on one machine")
                .And.Contain("When it can't reach Requestarr")
                .And.Contain("`#requestarr-feed`")
                .And.Contain("`pollingOnly`");
        }

        [Test]
        public void the_command_reference_covers_progress_add_seasons_and_the_feed()
        {
            var commands = File.ReadAllText(Item4To6Fixture.RepoFile("docs", "commands.md"));

            commands.Should().Contain("⬇️ **Downloading**").And.Contain("**Add seasons**").And.Contain("### The admin feed").And.Contain("never deleted");
        }
    }
}
