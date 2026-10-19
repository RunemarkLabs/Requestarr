using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Metadata;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Polish
{
    // Item 4: an optional The Movie Database key adds the top cast to result lines and the card.
    [TestFixture]
    public class Item4CastFixture : PolishScenario
    {
        private const string Key = "v3-secret-key-123";

        [Test]
        public async Task without_a_key_lines_show_year_network_and_genre()
        {
            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;

            results.SelectMenu.Options.Select(o => (o.Label, o.Description)).Should().Equal(
                ("Dark Matter (2024)", "2024 · Apple TV · Drama"),
                ("Dark Matter (2015)", "2015 · SYFY · Adventure"));
            results.Content.Should().NotContain("TMDB");
            Tmdb.Calls.Should().Be(0);
        }

        [Test]
        public async Task with_a_key_lines_show_the_top_three_cast()
        {
            TmdbKey.Set(Key);

            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;

            results.SelectMenu.Options.Select(o => o.Description).Should().Equal(
                "2024 · Apple TV · Joel Edgerton, Jennifer Connelly, Alice Braga",
                "2015 · SYFY · Marc Bendavid, Melissa O'Neil, Anthony Lemke");
            results.Content.Should().Contain(DiscordText.TmdbSubtext);
        }

        [Test]
        public void lines_stay_within_100_characters_by_dropping_cast_from_the_end()
        {
            var item = new MediaLookupResult
            {
                Year = 2024,
                Network = "A Very Long Network Name Indeed",
                Genres = { "Drama" },
                Cast = { "Christopher Abbott-Longname", "Elizabeth Mary Winstead-Smith", "Bartholomew Cubbins The Third" }
            };

            var line = RequestCards.ResultLine(item);

            line.Length.Should().BeLessThanOrEqualTo(DiscordLimits.OptionLabelLength);
            line.Should().Be("2024 · A Very Long Network Name Indeed · Christopher Abbott-Longname, Elizabeth Mary Winstead-Smith");

            item.Network = new string('N', 120);
            RequestCards.ResultLine(item).Length.Should().BeLessThanOrEqualTo(DiscordLimits.OptionLabelLength);
        }

        [Test]
        public async Task the_movie_database_failing_never_breaks_a_request()
        {
            TmdbKey.Set(Key);
            Tmdb.Fail = true;

            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;

            results.SelectMenu.Options.First().Description.Should().Be("2024 · Apple TV · Drama");
            Field(card, "Cast").Should().BeNull();
            card.Embeds.Single().Footer.Should().BeNull();
        }

        [Test]
        public async Task cast_is_cached_between_searches()
        {
            TmdbKey.Set(Key);

            await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"));

            Tmdb.Calls.Should().Be(2);
        }

        [Test]
        public void a_read_access_token_goes_in_the_header_and_a_v3_key_in_the_query()
        {
            const string token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ4In0.c2ln";

            using var bearer = TmdbClient.Build(ContentType.Tv, 196322, token);
            bearer.Headers.Authorization.Scheme.Should().Be("Bearer");
            bearer.RequestUri.ToString().Should().Be("https://api.themoviedb.org/3/tv/196322?append_to_response=aggregate_credits");

            using var v3 = TmdbClient.Build(ContentType.Movie, 438631, Key);
            v3.Headers.Authorization.Should().BeNull();
            v3.RequestUri.ToString().Should().Be($"https://api.themoviedb.org/3/movie/438631?append_to_response=credits&api_key={Key}");
        }

        [Test]
        public async Task the_client_reads_top_cast_and_episode_counts()
        {
            var client = Client(out _);
            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"number_of_episodes\":9,\"aggregate_credits\":{\"cast\":[{\"name\":\"C\",\"order\":2},{\"name\":\"A\",\"order\":0},{\"name\":\"B\",\"order\":1},{\"name\":\"D\",\"order\":3}]}}")
            });

            var details = await client.GetAsync(ContentType.Tv, 196322, CancellationToken.None);

            details.Cast.Should().Equal("A", "B", "C");
            details.EpisodeCount.Should().Be(9);
        }

        [Test]
        public async Task the_key_is_never_logged()
        {
            var client = Client(out var memory);

            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            (await client.GetAsync(ContentType.Movie, 1, CancellationToken.None)).Should().BeNull();

            client.Transport = (request, _) => throw new HttpRequestException($"failed {request.RequestUri}");
            (await client.GetAsync(ContentType.Movie, 2, CancellationToken.None)).Should().BeNull();

            memory.Logs.Should().HaveCount(2);
            memory.Logs.Should().OnlyContain(l => !l.Contains(Key));
            memory.Logs.First().Should().Contain("HTTP 401");
        }

        [Test]
        public void an_empty_key_disables_cast()
        {
            var store = new TmdbSettingsStore();

            store.Set("  ");
            store.Enabled.Should().BeFalse();
            store.Set($" {Key} ");
            store.ApiKey.Should().Be(Key);
        }

        private TmdbClient Client(out MemoryTarget memory)
        {
            memory = new MemoryTarget { Layout = "${message} ${exception:format=tostring}" };
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(memory);
            var factory = new LogFactory { Configuration = config };

            var store = new TmdbSettingsStore();
            store.Set(Key);

            return new TmdbClient(store, factory.GetLogger("tmdb-test"));
        }
    }
}
