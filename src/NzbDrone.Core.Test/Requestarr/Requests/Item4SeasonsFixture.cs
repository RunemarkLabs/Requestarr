using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Item 4: TV seasons at request time: specific, all, future. Uses the season menu on cards posted
    // before the two-path flow (CommandSurface/Polish fixtures cover the new one), which must keep working.
    [TestFixture]
    public class Item4SeasonsFixture : RequestScenario
    {
        [SetUp]
        public async Task AutoApproveTv()
        {
            await SetAccess(Alice, "tv", "auto");
        }

        [Test]
        public async Task long_shows_page_their_seasons_within_discord_limits()
        {
            var step = (await Harness.SendAsync(FakeInteraction.Button($"{RequestRoutes.TvChoose}:{LongShow}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).LastReply;

            step.SelectMenu.Options.Count.Should().Be(RequestCards.SeasonsPerPage);
            step.SelectMenu.Options[0].Value.Should().Be("s1");
            step.SelectMenu.Options[^1].Value.Should().Be("s24");
            step.Buttons.First().Label.Should().Be("Seasons 25–40 ▶");
        }

        [Test]
        public async Task specific_seasons_should_be_requested_and_monitored()
        {
            await RequestTv(Alice, Severance, "s2");

            LastRequest.Seasons.Should().Equal(2);
            LastRequest.MonitorFutureSeasons.Should().BeFalse();
            Sonarr.Adds.Single().Seasons.Should().Equal(2);
            Sonarr.Adds.Single().MonitorFuture.Should().BeFalse();
        }

        [Test]
        public async Task all_and_future_seasons()
        {
            await RequestTv(Alice, Severance, "all", "future");

            LastRequest.Seasons.Should().Equal(1, 2);
            LastRequest.MonitorFutureSeasons.Should().BeTrue();
            Sonarr.Adds.Single().MonitorFuture.Should().BeTrue();
            DiscordText.Seasons(LastRequest).Should().Be("seasons 1–2, plus new seasons as they air");
        }

        [Test]
        public async Task future_only_is_allowed()
        {
            await RequestTv(Alice, Severance, "future");

            LastRequest.Seasons.Should().BeEmpty();
            LastRequest.MonitorFutureSeasons.Should().BeTrue();
        }

        [Test]
        public async Task seasons_not_in_the_show_are_ignored_and_nothing_is_refused()
        {
            (await RequestTv(Alice, Severance, "s9")).LastReply.Content.Should().Contain(DiscordText.ChooseSeasons);
            RequestStore.All().Should().BeEmpty();
        }

        [Test]
        public void season_values_parse()
        {
            var choice = RequestCards.ParseSeasons(new[] { "s1", "s10", "future", "junk" });

            choice.All.Should().BeFalse();
            choice.Seasons.Should().Equal(1, 10);
            choice.Future.Should().BeTrue();
        }
    }
}
