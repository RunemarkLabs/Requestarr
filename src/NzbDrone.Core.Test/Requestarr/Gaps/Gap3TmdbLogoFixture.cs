using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Gaps
{
    // Gap 3: The Movie Database's approved logo and notice, only while a key is set.
    [TestFixture]
    public class Gap3TmdbLogoFixture : GapScenario
    {
        [Test]
        public async Task cards_with_cast_carry_the_small_tmdb_logo_in_the_footer()
        {
            TmdbKey.Set("test-key");

            var embed = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply.Embeds.Single();

            embed.Footer.Should().Be(DiscordText.TmdbAttribution);
            embed.FooterIconUrl.Should().Be(DiscordText.TmdbLogoUrl);
            embed.ThumbnailUrl.Should().BeNull("the logo is a footer icon, less prominent than the card");
        }

        [Test]
        public async Task without_a_key_there_is_no_logo_or_credit()
        {
            var embed = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply.Embeds.Single();

            embed.Footer.Should().BeNull();
            embed.FooterIconUrl.Should().BeNull();
        }

        [Test]
        public async Task cards_without_cast_have_no_logo_even_with_a_key()
        {
            TmdbKey.Set("test-key");

            var embed = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{DuneTwo}"))).LastReply.Embeds.Single();

            embed.FooterIconUrl.Should().BeNull();
        }

        [Test]
        public async Task the_admin_credits_topic_has_the_notice_and_logo()
        {
            TmdbKey.Set("test-key");

            var credits = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "credits").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            credits.LastReply.Content.Should().StartWith("**Credits**").And.Contain(DiscordText.TmdbNotice);
            credits.LastReply.Embeds.Single().Url.Should().Be(DiscordText.TmdbSite);
            credits.LastReply.Embeds.Single().ThumbnailUrl.Should().Be(DiscordText.TmdbLogoUrl);
        }

        [Test]
        public void the_notice_uses_the_wording_from_tmdbs_terms()
        {
            DiscordText.TmdbNotice.Should().Contain("uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB");
        }

        // Task 4 item 15: the images travel with the message, from inside Requestarr; nothing is loaded from GitHub.
        [Test]
        public async Task the_logo_and_the_feed_spacer_are_built_in_and_attached_to_the_message()
        {
            DiscordText.TmdbLogoUrl.Should().Be("attachment://tmdb-logo.png");
            DiscordText.FeedCardWidthUrl.Should().Be("attachment://feed-card-width.png");

            foreach (var name in new[] { DiscordAssets.TmdbLogo, DiscordAssets.FeedCardWidth })
            {
                var png = DiscordAssets.Bytes(name);
                png.Should().NotBeNull($"{name} is an embedded resource");
                png.Take(8).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "it is a PNG");
            }

            var spacer = DiscordAssets.Bytes(DiscordAssets.FeedCardWidth);
            (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(spacer.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(spacer.AsSpan(20))).Should().Be((800, 1));

            TmdbKey.Set("test-key");
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;
            DiscordAssets.For(card).Select(f => f.Name).Should().Equal(DiscordAssets.TmdbLogo);
            DiscordAssets.For(new Requestarr.Core.Discord.Interactions.InteractionReply { Content = "plain" }).Should().BeEmpty();
        }

        [Test]
        public void no_message_image_points_at_github()
        {
            var source = string.Join("\n", System.IO.Directory.GetFiles(AnimeDub.Item4To6Fixture.RepoFile("src", "NzbDrone.Core", "Requestarr"), "*.cs", System.IO.SearchOption.AllDirectories).Select(System.IO.File.ReadAllText));

            source.Should().NotContain("githubusercontent").And.NotContain("docs/assets");
        }
    }
}
