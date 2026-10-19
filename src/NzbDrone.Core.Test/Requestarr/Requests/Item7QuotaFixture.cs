using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // Item 7: rolling-window quotas count owned requests only; refusal says when a slot frees up.
    [TestFixture]
    public class Item7QuotaFixture : RequestScenario
    {
        private DateTime _now;

        [SetUp]
        public async Task Quota()
        {
            _now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
            ((RequestService)Requests).Clock = () => _now;
            await Harness.SendAsync(AdminCommand("user quota", Alice).WithOption("type", "movie").WithOption("limit", 2L).WithOption("days", 7L));
        }

        [Test]
        public async Task request_over_the_limit_should_be_refused_with_next_slot()
        {
            await RequestMovie(Alice, Dune);
            _now = _now.AddDays(1);
            await RequestMovie(Alice, DuneTwo);

            Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Heat", Year = 1995, TmdbId = 949 });
            var run = await RequestMovie(Alice, 949);

            run.LastReply.Content.Should().Contain(DiscordText.QuotaReached(ContentType.Movie, 2, 7, new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc)));
            RequestStore.All().Should().HaveCount(2);
        }

        [Test]
        public async Task requests_outside_the_window_free_up_slots()
        {
            await RequestMovie(Alice, Dune);
            await RequestMovie(Alice, DuneTwo);
            Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Heat", Year = 1995, TmdbId = 949 });

            _now = _now.AddDays(7);

            (await RequestMovie(Alice, 949)).LastReply.Content.Should().Contain(DiscordText.RequestPending("Heat (1995)", 3));
        }

        [Test]
        public async Task subscriptions_cancelled_and_denied_requests_do_not_count()
        {
            await RequestMovie(Bob, Dune);
            await RequestMovie(Alice, Dune);
            await RequestMovie(Alice, DuneTwo);
            await Harness.SendAsync(CancelButton(Alice, 2));
            Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Heat", Year = 1995, TmdbId = 949 });
            Radarr.Catalog.Add(new Backends.MediaLookupResult { Title = "Ronin", Year = 1998, TmdbId = 8195 });

            (await RequestMovie(Alice, 949)).LastReply.Content.Should().Contain(DiscordText.RequestPending("Heat (1995)", 3));
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 3L));
            (await RequestMovie(Alice, 8195)).LastReply.Content.Should().Contain(DiscordText.RequestPending("Ronin (1998)", 4));
        }

        [Test]
        public async Task quota_command_should_show_usage()
        {
            await RequestMovie(Alice, Dune);

            var run = await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyLimits));

            run.LastReply.Content.Should().Contain("Movies: needs approval, 1 of 2 used in the last 7 days").And.Contain("TV: needs approval, no limit");
        }

        [Test]
        public async Task each_user_sees_only_their_own_limits()
        {
            await RequestMovie(Alice, Dune);

            (await Harness.SendAsync(PanelButton(Alice, PanelRoutes.MyLimits))).LastReply.Content.Should().Contain("**alice**").And.Contain("1 of 2");
            (await Harness.SendAsync(PanelButton(Bob, PanelRoutes.MyLimits))).LastReply.Content.Should().Contain("**bob**").And.Contain("no limit");
        }
    }
}
