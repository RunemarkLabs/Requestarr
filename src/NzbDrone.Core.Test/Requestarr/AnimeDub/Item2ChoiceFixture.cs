using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.AnimeDub
{
    // Item 2: anime (genre "Anime") asks Sub or Dub before seasons; the language rides along to Sonarr.
    [TestFixture]
    public class Item2ChoiceFixture : AnimeScenario
    {
        [Test]
        public async Task an_anime_card_asks_dub_or_sub_and_states_the_rule()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{OnePiece}"))).LastReply;

            card.Content.Should().Contain(DiscordText.AnimeIntro).And.Contain("dub wins");
            card.Buttons.Select(b => (b.Label, b.CustomId)).Should().StartWith(new[] { ("Dub", $"req-tv-lang:{OnePiece}:d"), ("Sub", $"req-tv-lang:{OnePiece}:s") });
            card.Buttons.Should().NotContain(b => b.CustomId.StartsWith("req-tv-all"));
        }

        [Test]
        public async Task other_tv_is_not_asked()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{Severance}"))).LastReply;

            card.Content.Should().NotContain(DiscordText.AnimeIntro);
            card.Buttons.Select(b => b.CustomId).Should().Contain($"req-tv-all:{Severance}");
        }

        [Test]
        public async Task dub_then_all_seasons_adds_it_as_anime_tagged_dub()
        {
            var tags = await SetUp();
            var message = await Ask(Alice, "tv", $"tvdb:{OnePiece}");

            var step = (await ClickOn(Alice, message, $"req-tv-lang:{OnePiece}:d")).LastReply;
            step.Content.Should().Contain(DiscordText.LanguageLine(AnimeLanguage.Dub));
            step.Buttons.Select(b => b.CustomId).Should().Contain($"req-tv-all:{OnePiece}:d").And.Contain($"req-tv-choose:{OnePiece}:d");

            var final = (await ClickOn(Alice, message, $"req-tv-all:{OnePiece}:d")).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalSent).And.Contain(DiscordText.LanguageLine(AnimeLanguage.Dub));
            Sonarr.Adds.Single().SeriesType.Should().Be("anime");
            Sonarr.Adds.Single().Tags.Should().Equal(tags.Dub);
            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
        }

        [Test]
        public async Task sub_through_choose_seasons_keeps_the_language_through_every_step()
        {
            var tags = await SetUp();
            var message = await Ask(Alice, "tv", $"tvdb:{OnePiece}");
            await ClickOn(Alice, message, $"req-tv-lang:{OnePiece}:s");

            var picker = (await ClickOn(Alice, message, $"req-tv-choose:{OnePiece}:s")).LastReply;
            picker.SelectMenu.CustomId.Should().EndWith(":s");
            picker.Buttons.Where(b => b.CustomId.StartsWith("req-tv-page") || b.CustomId.StartsWith("req-tv-review")).Should().OnlyContain(b => b.CustomId.EndsWith(":s"));

            var picked = (await ChooseOn(Alice, message, picker.SelectMenu.CustomId, "s1", "s2")).LastReply;
            var confirm = (await ClickOn(Alice, message, picked.Buttons.Single(b => b.Label == DiscordText.ContinueLabel).CustomId)).LastReply;
            confirm.Content.Should().Contain(DiscordText.LanguageLine(AnimeLanguage.Sub));

            await ClickOn(Alice, message, confirm.Buttons.First().CustomId);

            Sonarr.Adds.Single().Tags.Should().Equal(tags.Sub);
            Sonarr.Adds.Single().Seasons.Should().Equal(1, 2);
            RequestFor().Language.Should().Be(AnimeLanguage.Sub);
        }

        [Test]
        public async Task the_card_says_what_an_existing_series_is_set_to()
        {
            var tags = await SetUp();
            Sonarr.AddToLibrary(OnePiece, "anime", 41, tags.Sub);

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{OnePiece}"))).LastReply;

            card.Content.Should().Contain(DiscordText.CurrentlySetTo(AnimeLanguage.Sub)).And.Contain("dub wins");
            card.Buttons.Select(b => (b.Label, b.CustomId)).Should().Contain(("Switch to Dub", $"req-tv-lang:{OnePiece}:d"));
        }

        [Test]
        public async Task no_switch_is_offered_when_it_would_lose()
        {
            var tags = await SetUp();
            Sonarr.AddToLibrary(OnePiece, "anime", tags.Dub);

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{OnePiece}"))).LastReply;

            card.Content.Should().Contain(DiscordText.CurrentlySetTo(AnimeLanguage.Dub));
            card.Buttons.Should().NotContain(b => b.Label.StartsWith("Switch to"));
        }

        [Test]
        public async Task an_old_season_button_on_an_anime_title_gets_the_rules_default_language()
        {
            var tags = await SetUp();

            await Click(Alice, $"req-tv-all:{OnePiece}");

            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            Sonarr.Adds.Single().Tags.Should().Equal(tags.Dub);
        }

        [Test]
        public async Task a_late_language_click_after_someone_requested_it_is_settled_by_the_rule()
        {
            await SetAccess(Alice, "tv", "approval");
            await RequestAnime(Alice, "d");

            var run = await AskLanguage(Bob, "s");

            RequestStore.All().Should().ContainSingle();
            RequestFor().Language.Should().Be(AnimeLanguage.Dub);
            run.LastReply.Content.Should().StartWith(DiscordText.FinalFollowing).And.Contain("stays **Dub**");
        }
    }
}
