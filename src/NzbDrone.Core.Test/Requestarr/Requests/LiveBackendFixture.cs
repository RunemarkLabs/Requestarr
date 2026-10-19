using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Metadata;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // Live: requests through the real dispatcher and handlers into the dev stack's TEST
    // Sonarr and Radarr only (localhost ports from docker/compose.yaml), then reads back what
    // those instances stored. Titles are left there for the owner to inspect.
    //   set -a; . docker/secrets.env; set +a
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveBackendFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveBackendFixture : DiscordScenario
    {
        private const int Matrix = 603;
        private const int Severance = 371980;

        private static string RadarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("RADARR_TEST_PORT") ?? "17878"}";
        private static string SonarrUrl => $"http://localhost:{Environment.GetEnvironmentVariable("SONARR_TEST_PORT") ?? "18989"}";
        private static string RadarrKey => Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY");
        private static string SonarrKey => Environment.GetEnvironmentVariable("SONARR_TEST_API_KEY");

        protected override void UseBackends()
        {
            if (string.IsNullOrWhiteSpace(RadarrKey) || string.IsNullOrWhiteSpace(SonarrKey))
            {
                Assert.Ignore("RADARR_TEST_API_KEY / SONARR_TEST_API_KEY not set");
            }

            UseRealHttp();
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Radarr (test)", Url = RadarrUrl, ApiKey = RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true }
                },
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Sonarr (test)", Url = SonarrUrl, ApiKey = SonarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/tv", SeriesType = "standard", Default = true }
                }
            });
        }

        // The real The Movie Database client when TMDB_API_KEY is set; otherwise no cast.
        protected override void UseMetadata()
        {
            var key = Environment.GetEnvironmentVariable("TMDB_API_KEY");
            TmdbKey = new TmdbSettingsStore();
            TmdbKey.Set(key);
            Mocker.SetConstant<ITmdbSettingsStore>(TmdbKey);
            Mocker.SetConstant<ITmdbClient>(new TmdbClient(TmdbKey, NLog.LogManager.GetLogger("LiveTmdb")));
        }

        // Both season paths through the real handlers into the test Sonarr, then per-season
        // monitoring and the new-seasons setting read back from Sonarr itself.
        [Test]
        public async Task tv_requests_via_both_season_paths_read_back_from_sonarr()
        {
            Log($"[tmdb] key configured: {TmdbKey.Enabled}");
            await AddUser(Alice);
            await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "tv").WithOption("level", "auto"));

            var results = await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            Log($"[results] {results.LastReply.Content.Replace("\n", " | ")}");

            foreach (var option in results.LastReply.SelectMenu.Options.Take(5))
            {
                Log($"[results]   {option.Label}  —  {option.Description}");
            }

            var newer = int.Parse(results.LastReply.SelectMenu.Options.First(o => o.Label == "Dark Matter (2024)").Value);
            var older = int.Parse(results.LastReply.SelectMenu.Options.First(o => o.Label == "Dark Matter (2015)").Value);

            // Path 1: All seasons.
            var card = await Harness.SendAsync(FakeInteraction.SelectMenu(results.LastReply.SelectMenu.CustomId, newer.ToString()).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            LogCard("card", card.LastReply);

            var allFinal = await Click($"{RequestRoutes.TvAll}:{newer}");
            Log($"[all] final (no confirmation step): {allFinal.LastReply.Content.Replace("\n", " | ")}");

            // Path 2: Choose seasons, season 2 only.
            var choose = await Click($"{RequestRoutes.TvChoose}:{older}");
            Log($"[choose] menu: {string.Join(", ", choose.LastReply.SelectMenu.Options.Select(o => $"{o.Label}={o.Value}"))}");
            var picked = await Harness.SendAsync(FakeInteraction.SelectMenu($"{RequestRoutes.TvPick}:{older}", "s2").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            Log($"[choose] confirm: {picked.LastReply.Content.Replace("\n", " | ")}");
            var chooseFinal = await Click(picked.LastReply.Buttons.First().CustomId);
            Log($"[choose] final: {chooseFinal.LastReply.Content.Replace("\n", " | ")}");

            var http = Mocker.Resolve<IHttpClient>();
            var series = (await Get(http, SonarrUrl, SonarrKey, "series")).Children<JObject>().ToList();
            var allSeries = series.Single(s => (int)s["tvdbId"] == newer);
            var chosenSeries = series.Single(s => (int)s["tvdbId"] == older);

            foreach (var (label, s) in new[] { ("all", allSeries), ("choose", chosenSeries) })
            {
                Log($"[sonarr] {label}: {s["title"]} ({s["year"]}) monitored={s["monitored"]} monitorNewItems={s["monitorNewItems"]}");
                Log($"[sonarr] {label}: seasons {string.Join(", ", s["seasons"].Select(x => $"S{x["seasonNumber"]}={((bool)x["monitored"] ? "monitored" : "unmonitored")}"))}");
            }

            ((string)allSeries["monitorNewItems"]).Should().Be("all");
            allSeries["seasons"].Where(x => (int)x["seasonNumber"] > 0).Should().OnlyContain(x => (bool)x["monitored"]);
            allSeries["seasons"].Where(x => (int)x["seasonNumber"] == 0).Should().OnlyContain(x => !(bool)x["monitored"]);

            ((string)chosenSeries["monitorNewItems"]).Should().Be("none");
            chosenSeries["seasons"].Single(x => (int)x["seasonNumber"] == 2)["monitored"].Value<bool>().Should().BeTrue();
            chosenSeries["seasons"].Where(x => (int)x["seasonNumber"] != 2).Should().OnlyContain(x => !(bool)x["monitored"]);

            // A movie card from the real Radarr, for the record.
            var movie = await Harness.SendAsync(Search(Alice, "movie", "tmdb:438631"));
            LogCard("movie card", movie.LastReply);
        }

        private Task<InteractionRun> Click(string customId) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

        private static void LogCard(string label, InteractionReply reply)
        {
            var embed = reply.Embeds.Single();
            Log($"[{label}] {reply.Content.Replace("\n", " | ")}");
            Log($"[{label}] title=\"{embed.Title}\" link={embed.Url ?? "none"} poster={(embed.ImageUrl != null ? "large image" : "none")} colour=#{embed.Color:X6} footer={embed.Footer ?? "none"}");
            Log($"[{label}] overview: {DiscordLimits.Truncate(embed.Description, 90)}");

            foreach (var field in embed.Fields)
            {
                Log($"[{label}]   {field.Key}: {field.Value.Replace("\n", " / ")}");
            }

            Log($"[{label}] buttons: {string.Join(" ", reply.Buttons.Select(b => $"[{b.Label}]"))}");
        }

        [Test]
        public async Task request_movie_and_tv_into_test_instances_and_read_back()
        {
            var backends = Mocker.Resolve<IBackendService>();
            await backends.CheckAllAsync();

            foreach (var result in backends.LastResults)
            {
                Log($"[check] {result.Name}: {(result.Ok ? "ok" : result.Error)}");
            }

            backends.LastResults.Should().OnlyContain(r => r.Ok);

            await AddUser(Alice);
            await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "movie").WithOption("level", "auto"));
            await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "tv").WithOption("level", "approval"));

            // Movie: search through Radarr's own lookup, then request (auto-approve).
            var search = await Harness.SendAsync(Search(Alice, "movie", "The Matrix"));
            Log($"[movie] search: {search.LastReply.Content}");

            foreach (var option in search.LastReply.SelectMenu?.Options.Take(3) ?? Enumerable.Empty<Core.Discord.Interactions.InteractionSelectOption>())
            {
                Log($"[movie]   option: {option.Label} (tmdb {option.Value})");
            }

            var movieRun = await Harness.SendAsync(FakeInteraction.Button($"{RequestRoutes.Make}:movie:{Matrix}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            Log($"[movie] request: {movieRun.LastReply.Content}");

            // TV: season 1 plus future seasons, needs approval, approved from the admin card.
            var tvRun = await Harness.SendAsync(FakeInteraction.SelectMenu($"{RequestRoutes.Seasons}:{Severance}", "s1", "future").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            Log($"[tv] request: {tvRun.LastReply.Content}");

            var tvRequest = Mocker.Resolve<IRequestRepository>().All().SingleOrDefault(r => r.ContentType == ContentType.Tv);

            if (tvRequest != null)
            {
                var approve = await Harness.SendAsync(FakeInteraction.Button($"{RequestRoutes.Approve}:{tvRequest.Id}").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
                Log($"[tv] admin approve: {approve.LastReply.Content}");
                Log($"[tv] user notice: {LastMessage(Alice)}");
            }

            // Read back from the test instances themselves.
            var http = Mocker.Resolve<IHttpClient>();
            var movie = (await Get(http, RadarrUrl, RadarrKey, "movie")).Children<JObject>().Single(m => (int)m["tmdbId"] == Matrix);
            var radarrProfiles = (await Get(http, RadarrUrl, RadarrKey, "qualityprofile")).Children<JObject>().ToDictionary(p => (int)p["id"], p => (string)p["name"]);
            Log($"[radarr] {movie["title"]} ({movie["year"]}) id={movie["id"]} profile={radarrProfiles[(int)movie["qualityProfileId"]]} root={movie["rootFolderPath"]} path={movie["path"]} monitored={movie["monitored"]}");

            var series = (await Get(http, SonarrUrl, SonarrKey, "series")).Children<JObject>().Single(s => (int)s["tvdbId"] == Severance);
            var sonarrProfiles = (await Get(http, SonarrUrl, SonarrKey, "qualityprofile")).Children<JObject>().ToDictionary(p => (int)p["id"], p => (string)p["name"]);
            var seasons = string.Join(", ", series["seasons"].Select(s => $"S{s["seasonNumber"]}={((bool)s["monitored"] ? "monitored" : "unmonitored")}"));
            Log($"[sonarr] {series["title"]} ({series["year"]}) id={series["id"]} profile={sonarrProfiles[(int)series["qualityProfileId"]]} root={series["rootFolderPath"]} type={series["seriesType"]} monitored={series["monitored"]} monitorNewItems={series["monitorNewItems"]}");
            Log($"[sonarr] seasons: {seasons}");

            radarrProfiles[(int)movie["qualityProfileId"]].Should().Be("HD-1080p");
            ((string)movie["rootFolderPath"]).TrimEnd('/').Should().Be("/data/movies");
            ((bool)movie["monitored"]).Should().BeTrue();

            sonarrProfiles[(int)series["qualityProfileId"]].Should().Be("HD-1080p");
            ((string)series["rootFolderPath"]).TrimEnd('/').Should().Be("/data/tv");
            ((string)series["seriesType"]).Should().Be("standard");
            ((string)series["monitorNewItems"]).Should().Be("all");
            series["seasons"].Single(s => (int)s["seasonNumber"] == 1)["monitored"].Value<bool>().Should().BeTrue();
            series["seasons"].Where(s => (int)s["seasonNumber"] > 1).Should().OnlyContain(s => !(bool)s["monitored"]);

            foreach (var request in Mocker.Resolve<IRequestRepository>().All())
            {
                Log($"[requestarr] request #{request.Id} {request.ContentType} {request.Label} status={request.Status} item={request.ExternalItemId} seasons={Core.Discord.DiscordText.Seasons(request)}");
            }
        }

        private static async Task<JArray> Get(IHttpClient http, string url, string key, string resource)
        {
            var request = new HttpRequestBuilder($"{url}/api/v3/").Resource(resource).SetHeader("X-Api-Key", key).Build();
            return JArray.Parse((await http.GetAsync(request)).Content);
        }

        private string LastMessage(ulong user) => ChannelFor(user).Messages.LastOrDefault()?.Content;

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
