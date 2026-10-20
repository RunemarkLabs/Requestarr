using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Gaps
{
    // Gap 2: Choose seasons pages through every season of any show in the same message, keeping
    // picks across pages, and the confirmation states exactly what was picked.
    [TestFixture]
    public class Gap2SeasonPagesFixture : GapScenario
    {
        private ulong _message;

        [SetUp]
        public async Task AutoApproveTv()
        {
            await SetAccess(Alice, "tv", "auto");
        }

        private async Task<InteractionReply> Open(int tvdbId)
        {
            _message = await Ask(Alice, "tv", $"tvdb:{tvdbId}");
            return (await ClickOn(Alice, _message, $"req-tv-choose:{tvdbId}")).LastReply;
        }

        private async Task<InteractionReply> Press(InteractionReply step, string label) =>
            (await ClickOn(Alice, _message, step.Buttons.Single(b => b.Label == label).CustomId)).LastReply;

        private async Task<InteractionReply> Pick(InteractionReply step, params string[] values) =>
            (await ChooseOn(Alice, _message, step.SelectMenu.CustomId, values)).LastReply;

        private static string[] Values(InteractionReply step) => step.SelectMenu.Options.Select(o => o.Value).ToArray();

        private static string[] Selected(InteractionReply step) => step.SelectMenu.Options.Where(o => o.Default).Select(o => o.Value).ToArray();

        [Test]
        public async Task a_36_season_show_pages_specials_and_seasons_1_to_24_then_25_to_36()
        {
            var first = await Open(ThirtySix);

            Values(first).Should().Equal(new[] { "s0" }.Concat(Enumerable.Range(1, 24).Select(n => $"s{n}")));
            first.SelectMenu.Options.Count.Should().BeLessThanOrEqualTo(DiscordLimits.ChoicesPerMenu);
            first.Content.Should().Contain("Showing seasons 1–24 of 36");
            first.Buttons.Select(b => b.Label).Should().Equal("Seasons 25–36 ▶", "Also new seasons: off", "Continue", "Back");

            var second = await Press(first, "Seasons 25–36 ▶");

            Values(second).Should().Equal(Enumerable.Range(25, 12).Select(n => $"s{n}"));
            second.Buttons.First().Label.Should().Be("◀ Seasons 1–24");
        }

        [Test]
        public async Task picks_from_different_pages_are_kept_and_confirmed()
        {
            var first = await Open(ThirtySix);
            first = await Pick(first, "s1", "s2");
            first.Content.Should().Contain("Picked so far: **seasons 1–2 only; new seasons won't be added**.");

            var second = await Press(first, "Seasons 25–36 ▶");
            Selected(second).Should().BeEmpty();
            second = await Pick(second, "s36");
            second.Content.Should().Contain("Picked so far: **seasons 1–2 and 36 only");

            var back = await Press(second, "◀ Seasons 1–24");
            Selected(back).Should().Equal("s1", "s2");

            var confirm = await Press(back, "Continue");
            confirm.Content.Should().Contain("You chose **seasons 1–2 and 36 only; new seasons won't be added**.");

            var final = (await ClickOn(Alice, _message, confirm.Buttons.First().CustomId)).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalSent).And.Contain("Seasons: seasons 1–2 and 36 only");
            Sonarr.Adds.Single().Seasons.Should().Equal(1, 2, 36);
            Sonarr.Adds.Single().MonitorFuture.Should().BeFalse();
        }

        [Test]
        public async Task unticking_on_a_page_only_changes_that_page()
        {
            var first = await Open(ThirtySix);
            first = await Pick(first, "s1", "s2");
            var second = await Pick(await Press(first, "Seasons 25–36 ▶"), "s30");
            first = await Press(second, "◀ Seasons 1–24");

            first = await Pick(first, "s2");

            first.Content.Should().Contain("Picked so far: **seasons 2 and 30 only");
        }

        [Test]
        public async Task specials_and_new_seasons_can_be_added_from_any_page()
        {
            var first = await Pick(await Open(ThirtySix), "s0");
            var second = await Press(first, "Seasons 25–36 ▶");
            second = await Press(second, "Also new seasons: off");
            second.Buttons.Single(b => b.Label.StartsWith("Also new")).Label.Should().Be("Also new seasons: on");

            var confirm = await Press(second, "Continue");

            confirm.Content.Should().Contain("You chose **specials, plus new seasons as they air**.");
            await ClickOn(Alice, _message, confirm.Buttons.First().CustomId);
            Sonarr.Adds.Single().Seasons.Should().Equal(0);
            Sonarr.Adds.Single().MonitorFuture.Should().BeTrue();
        }

        [Test]
        public async Task back_from_the_confirmation_keeps_the_picks()
        {
            var confirm = await Press(await Pick(await Open(ThirtySix), "s3"), "Continue");

            var again = await Press(confirm, "Back");

            Selected(again).Should().Equal("s3");
            again.Content.Should().Contain("Picked so far: **season 3 only");
        }

        [Test]
        public async Task continue_is_disabled_until_something_is_picked()
        {
            var first = await Open(ThirtySix);

            first.Buttons.Single(b => b.Label == "Continue").Disabled.Should().BeTrue();
            (await Pick(first, "s5")).Buttons.Single(b => b.Label == "Continue").Disabled.Should().BeFalse();
        }

        [Test]
        public async Task any_season_of_a_1000_plus_episode_anime_can_be_picked()
        {
            var first = await Open(OnePiece);

            Values(first).Should().Equal(new[] { "s0" }.Concat(Enumerable.Range(1, 22).Select(n => $"s{n}")));

            var confirm = await Press(await Pick(first, "s1", "s22"), "Continue");
            confirm.Content.Should().Contain("You chose **seasons 1 and 22 only");
            await ClickOn(Alice, _message, confirm.Buttons.First().CustomId);

            Sonarr.Adds.Single().Seasons.Should().Equal(1, 22);
        }

        [Test]
        public async Task scattered_picks_on_a_long_show_still_fit_discord_custom_ids()
        {
            var odd = new SeasonChoice(false, Enumerable.Range(0, 20).Select(n => (n * 2) + 1).ToList(), true);
            var spec = RequestCards.EncodeSeasons(odd);

            spec.Should().StartWith("b");
            RequestCards.DecodeSeasons(spec).Seasons.Should().Equal(odd.Seasons);
            RequestCards.DecodeSeasons(spec).Future.Should().BeTrue();
            $"{RequestRoutes.TvPick}:{ThirtySix}:1:{spec}".Length.Should().BeLessThanOrEqualTo(DiscordLimits.CustomIdLength);
        }

        [Test]
        public async Task every_button_and_menu_on_every_page_fits_discord_limits()
        {
            var step = await Open(LongShow);
            step = await Pick(step, Enumerable.Range(1, 24).Where(n => n % 3 == 0).Select(n => $"s{n}").ToArray());
            var next = await Press(step, "Seasons 25–40 ▶");

            foreach (var reply in new[] { step, next })
            {
                reply.SelectMenu.CustomId.Length.Should().BeLessThanOrEqualTo(DiscordLimits.CustomIdLength);
                reply.SelectMenu.Options.Count.Should().BeLessThanOrEqualTo(DiscordLimits.ChoicesPerMenu);
                reply.Buttons.Should().OnlyContain(b => b.CustomId.Length <= DiscordLimits.CustomIdLength);
            }
        }
    }
}
