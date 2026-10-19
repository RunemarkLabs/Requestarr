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
            var search = await Harness.SendAsync(UserCommand("request movie", Alice).WithOption("query", "The Matrix"));
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
