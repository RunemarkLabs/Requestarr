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
            DiscordText.TmdbLogoUrl.Should().StartWith("https://raw.githubusercontent.com/RunemarkLabs/Requestarr/main/").And.EndWith(".png");
        }
    }
}
