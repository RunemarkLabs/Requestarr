using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Requests
{
    // Item 4: TV seasons at request time: specific, all, future.
    [TestFixture]
    public class Item4SeasonsFixture : RequestScenario
    {
        [SetUp]
        public async Task AutoApproveTv()
        {
            await SetAccess(Alice, "tv", "auto");
        }

        [Test]
        public async Task tv_detail_should_offer_all_each_season_and_future()
        {
            var card = (await Harness.SendAsync(UserCommand("request tv-tvdb", Alice).WithOption("id", (long)Severance))).LastReply;

            card.SelectMenu.CustomId.Should().Be($"req-seasons:{Severance}");
            card.SelectMenu.Options.Select(o => o.Value).Should().Equal("all", "s1", "s2", "future");
            card.SelectMenu.MaxValues.Should().Be(4);
        }

        [Test]
        public async Task long_shows_should_offer_the_latest_seasons_within_25_options()
        {
            var card = (await Harness.SendAsync(UserCommand("request tv-tvdb", Alice).WithOption("id", (long)LongShow))).LastReply;

            card.SelectMenu.Options.Count.Should().Be(DiscordLimits.ChoicesPerMenu);
            card.SelectMenu.Options[1].Value.Should().Be("s18");
            card.SelectMenu.Options[^2].Value.Should().Be("s40");
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
            DiscordText.Seasons(LastRequest).Should().Be("1, 2 + future seasons");
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
            (await RequestTv(Alice, Severance, "s9")).LastReply.Content.Should().Be(DiscordText.ChooseSeasons);
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
