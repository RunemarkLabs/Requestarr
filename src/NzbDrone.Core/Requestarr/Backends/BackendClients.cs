using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace Requestarr.Core.Backends
{
    public record BackendStatus(string AppName, Version Version);

    public record NamedItem(int Id, string Name);

    // A Sonarr release profile (v4: required / ignored are lists of terms).
    public class ReleaseProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public List<string> Required { get; set; } = new();
        public List<string> Ignored { get; set; } = new();
        public int IndexerId { get; set; }
        public List<int> Tags { get; set; } = new();
    }

    // A title as returned by a backend's own lookup (Radarr / Sonarr metadata; no third-party keys).
    public class MediaLookupResult
    {
        public string Title { get; set; }
        public int? Year { get; set; }
        public string Overview { get; set; }
        public string PosterUrl { get; set; }

        // Movies: the request ID. TV: Sonarr's link to The Movie Database (cast lookups only).
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string ImdbId { get; set; }

        // Set when the title is already in the instance's library.
        public int? LibraryId { get; set; }
        public bool HasFile { get; set; }

        // Regular seasons (1 and up); season 0 is specials, only monitored when picked explicitly.
        public List<int> Seasons { get; set; } = new();
        public bool HasSpecials { get; set; }
        public bool Continuing { get; set; }

        // Card details, as the backend returns them.
        public List<string> Genres { get; set; } = new();
        public string Certification { get; set; }
        public int? Runtime { get; set; }
        public string Studio { get; set; }
        public string Network { get; set; }
        public string Status { get; set; }
        public List<(string Source, double Value)> Ratings { get; set; } = new();
        public long? Votes { get; set; }
        public DateTime? FirstAired { get; set; }
        public DateTime? LastAired { get; set; }
        public DateTime? InCinemas { get; set; }
        public DateTime? DigitalRelease { get; set; }
        public DateTime? PhysicalRelease { get; set; }

        // From The Movie Database when a key is configured; otherwise empty / null.
        public List<string> Cast { get; set; } = new();
        public int? EpisodeCount { get; set; }

        // TV: the series' tags and type on the instance (when it is in the library).
        public List<int> TagIds { get; set; } = new();
        public string SeriesType { get; set; }

        // Sonarr's metadata marks anime with the genre "Anime".
        public bool IsAnime => Genres.Any(g => string.Equals(g, "Anime", StringComparison.OrdinalIgnoreCase));

        // Studio for movies, network for TV.
        public string Maker => Studio ?? Network;

        // Raw lookup JSON, posted back when adding (backends expect their own lookup shape).
        public string Raw { get; set; }
    }

    public class BackendException : Exception
    {
        public BackendException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    public interface IBackendApi
    {
        Task<BackendStatus> GetStatusAsync(BackendSettings settings);
        Task<List<NamedItem>> GetQualityProfilesAsync(BackendSettings settings);
        Task<List<string>> GetRootFoldersAsync(BackendSettings settings);
    }

    public interface IRadarrApi : IBackendApi
    {
        Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term);
        Task<MediaLookupResult> LookupTmdbAsync(BackendSettings settings, int tmdbId);
        Task<int> AddMovieAsync(BackendSettings settings, MediaLookupResult movie, int qualityProfileId, string rootFolder);
    }

    public interface ISonarrApi : IBackendApi
    {
        Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term);
        Task<MediaLookupResult> LookupTvdbAsync(BackendSettings settings, int tvdbId);
        Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture, IReadOnlyCollection<int> tags = null);

        Task<List<NamedItem>> GetTagsAsync(BackendSettings settings);
        Task<NamedItem> CreateTagAsync(BackendSettings settings, string label);
        Task<List<ReleaseProfile>> GetReleaseProfilesAsync(BackendSettings settings);
        Task<ReleaseProfile> SaveReleaseProfileAsync(BackendSettings settings, ReleaseProfile profile);

        // Replaces a series' tags (the rest of the series is sent back unchanged).
        Task SetSeriesTagsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> tags);
    }

    // Shared v3 API plumbing (Sonarr 4 and Radarr 5 both use /api/v3).
    public abstract class ArrApiBase : IBackendApi
    {
        private readonly IHttpClient _httpClient;

        protected ArrApiBase(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<BackendStatus> GetStatusAsync(BackendSettings settings)
        {
            var status = await GetAsync<StatusResource>(settings, "system/status");
            return new BackendStatus(status.AppName, Version.TryParse(status.Version, out var v) ? v : new Version(0, 0));
        }

        public async Task<List<NamedItem>> GetQualityProfilesAsync(BackendSettings settings)
        {
            return (await GetAsync<List<NamedItem>>(settings, "qualityprofile")) ?? new List<NamedItem>();
        }

        public async Task<List<string>> GetRootFoldersAsync(BackendSettings settings)
        {
            return ((await GetAsync<List<RootFolderResource>>(settings, "rootfolder")) ?? new List<RootFolderResource>()).Select(r => r.Path).ToList();
        }

        protected async Task<T> GetAsync<T>(BackendSettings settings, string resource, params (string Key, object Value)[] query)
            where T : new()
        {
            var builder = Builder(settings, resource);

            foreach (var (key, value) in query)
            {
                builder.AddQueryParam(key, value);
            }

            try
            {
                var response = await _httpClient.GetAsync<T>(builder.Build());
                return response.Resource;
            }
            catch (Exception ex) when (ex is not BackendException)
            {
                throw new BackendException(Describe(settings, ex), ex);
            }
        }

        protected Task<string> PostAsync(BackendSettings settings, string resource, object body) => SendAsync(settings, resource, body, put: false);

        protected Task<string> PutAsync(BackendSettings settings, string resource, object body) => SendAsync(settings, resource, body, put: true);

        private async Task<string> SendAsync(BackendSettings settings, string resource, object body, bool put)
        {
            var builder = Builder(settings, resource);
            var request = builder.Post().Build();

            if (put)
            {
                request.Method = System.Net.Http.HttpMethod.Put;
            }

            request.Headers.ContentType = "application/json";
            request.SetContent(Json.ToJson(body));

            try
            {
                var response = await _httpClient.ExecuteAsync(request);
                return response.Content;
            }
            catch (HttpException ex)
            {
                throw new BackendException($"{Describe(settings, ex)} {Truncate(ex.Response?.Content)}", ex);
            }
            catch (Exception ex) when (ex is not BackendException)
            {
                throw new BackendException(Describe(settings, ex), ex);
            }
        }

        private static HttpRequestBuilder Builder(BackendSettings settings, string resource)
        {
            return new HttpRequestBuilder(settings.Url.TrimEnd('/') + "/api/v3/")
                .Resource(resource)
                .SetHeader("X-Api-Key", settings.ApiKey)
                .Accept(HttpAccept.Json);
        }

        private static string Describe(BackendSettings settings, Exception ex) => $"{settings.Url}: {ex.Message}";

        private static string Truncate(string value) => value == null || value.Length <= 300 ? value : value.Substring(0, 300);

        protected static string Text(Newtonsoft.Json.Linq.JToken token)
        {
            var value = token?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)token : null;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        protected static DateTime? Date(Newtonsoft.Json.Linq.JToken token) =>
            token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Date ? ((DateTime)token).ToUniversalTime()
            : DateTime.TryParse(Text(token), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

        protected static List<string> Genres(Newtonsoft.Json.Linq.JObject json) =>
            json["genres"]?.Select(g => (string)g).Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? new List<string>();

        protected static string Poster(List<ImageResource> images, string remotePoster = null)
        {
            return remotePoster ?? images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;
        }

        private class StatusResource
        {
            public string AppName { get; set; }
            public string Version { get; set; }
        }

        private class RootFolderResource
        {
            public string Path { get; set; }
        }

        protected class ImageResource
        {
            public string CoverType { get; set; }
            public string RemoteUrl { get; set; }
        }
    }

    public class RadarrApi : ArrApiBase, IRadarrApi
    {
        public RadarrApi(IHttpClient httpClient)
            : base(httpClient)
        {
        }

        public async Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term)
        {
            var results = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "movie/lookup", ("term", term));
            return (results ?? new List<Newtonsoft.Json.Linq.JObject>()).Select(Map).ToList();
        }

        public async Task<MediaLookupResult> LookupTmdbAsync(BackendSettings settings, int tmdbId)
        {
            var result = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, "movie/lookup/tmdb", ("tmdbId", tmdbId));

            if (result == null || (int?)result["tmdbId"] is null or 0)
            {
                return null;
            }

            // Radarr's lookup by TMDB ID doesn't say whether the movie is in the library; ask it.
            var movie = Map(result);
            var owned = (await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "movie", ("tmdbId", tmdbId)))?.FirstOrDefault();

            if (owned != null && (int?)owned["id"] is > 0)
            {
                movie.LibraryId = (int)owned["id"];
                movie.HasFile = (bool?)owned["hasFile"] ?? false;
            }

            return movie;
        }

        public async Task<int> AddMovieAsync(BackendSettings settings, MediaLookupResult movie, int qualityProfileId, string rootFolder)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(movie.Raw);
            body["qualityProfileId"] = qualityProfileId;
            body["rootFolderPath"] = rootFolder;
            body["monitored"] = true;
            body["minimumAvailability"] = "released";
            body["addOptions"] = Newtonsoft.Json.Linq.JObject.FromObject(new { searchForMovie = true, monitor = "movieOnly" });

            var response = Newtonsoft.Json.Linq.JObject.Parse(await PostAsync(settings, "movie", body));
            return (int)response["id"];
        }

        private static MediaLookupResult Map(Newtonsoft.Json.Linq.JObject json)
        {
            var images = json["images"]?.ToObject<List<ImageResource>>();
            var id = (int?)json["id"];

            return new MediaLookupResult
            {
                Title = (string)json["title"],
                Year = (int?)json["year"] is > 0 ? (int?)json["year"] : null,
                Overview = (string)json["overview"],
                PosterUrl = Poster(images, (string)json["remotePoster"]),
                TmdbId = (int?)json["tmdbId"],
                ImdbId = Text(json["imdbId"]),
                LibraryId = id is > 0 ? id : null,
                HasFile = (bool?)json["hasFile"] ?? false,
                Genres = Genres(json),
                Certification = Text(json["certification"]),
                Runtime = (int?)json["runtime"] is > 0 ? (int?)json["runtime"] : null,
                Studio = Text(json["studio"]),
                Status = Text(json["status"]),
                Ratings = new[] { ("IMDb", json["ratings"]?["imdb"]?["value"]), ("TMDB", json["ratings"]?["tmdb"]?["value"]), ("Rotten Tomatoes", json["ratings"]?["rottenTomatoes"]?["value"]) }
                    .Where(r => (double?)r.Item2 is > 0).Select(r => (r.Item1, (double)r.Item2)).ToList(),
                InCinemas = Date(json["inCinemas"]),
                DigitalRelease = Date(json["digitalRelease"]),
                PhysicalRelease = Date(json["physicalRelease"]),
                Raw = json.ToString(Newtonsoft.Json.Formatting.None)
            };
        }
    }

    public class SonarrApi : ArrApiBase, ISonarrApi
    {
        public SonarrApi(IHttpClient httpClient)
            : base(httpClient)
        {
        }

        public async Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term)
        {
            var results = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "series/lookup", ("term", term));
            return (results ?? new List<Newtonsoft.Json.Linq.JObject>()).Select(Map).ToList();
        }

        public async Task<MediaLookupResult> LookupTvdbAsync(BackendSettings settings, int tvdbId)
        {
            var series = (await LookupAsync(settings, $"tvdb:{tvdbId}")).FirstOrDefault(r => r.TvdbId == tvdbId);

            // A series already in the library: its id, tags and series type come from the library.
            if (series != null)
            {
                var owned = (await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "series", ("tvdbId", tvdbId)))?.FirstOrDefault(s => (int?)s["tvdbId"] == tvdbId);

                if (owned != null && (int?)owned["id"] is > 0)
                {
                    series.LibraryId = (int)owned["id"];
                    series.TagIds = owned["tags"]?.Select(t => (int)t).ToList() ?? new List<int>();
                    series.SeriesType = Text(owned["seriesType"]);
                }
            }

            return series;
        }

        public async Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture, IReadOnlyCollection<int> tags = null)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(series.Raw);
            body["qualityProfileId"] = qualityProfileId;
            body["rootFolderPath"] = rootFolder;
            body["seriesType"] = string.IsNullOrWhiteSpace(seriesType) ? "standard" : seriesType.ToLowerInvariant();
            body["monitored"] = true;
            body["seasonFolder"] = true;
            body["monitorNewItems"] = monitorFuture ? "all" : "none";

            var seasonArray = new Newtonsoft.Json.Linq.JArray();

            foreach (var number in (series.HasSpecials ? new[] { 0 } : Array.Empty<int>()).Concat(series.Seasons))
            {
                seasonArray.Add(Newtonsoft.Json.Linq.JObject.FromObject(new { seasonNumber = number, monitored = seasons.Contains(number) }));
            }

            body["seasons"] = seasonArray;

            if (tags != null && tags.Count > 0)
            {
                body["tags"] = new Newtonsoft.Json.Linq.JArray(tags);
            }

            // No "monitor" preset: Sonarr then keeps the per-season flags above.
            body["addOptions"] = Newtonsoft.Json.Linq.JObject.FromObject(new { searchForMissingEpisodes = true, searchForCutoffUnmetEpisodes = false, ignoreEpisodesWithFiles = true });

            var response = Newtonsoft.Json.Linq.JObject.Parse(await PostAsync(settings, "series", body));
            return (int)response["id"];
        }

        public async Task<List<NamedItem>> GetTagsAsync(BackendSettings settings) =>
            ((await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "tag")) ?? new List<Newtonsoft.Json.Linq.JObject>())
                .Select(t => new NamedItem((int)t["id"], (string)t["label"])).ToList();

        public async Task<NamedItem> CreateTagAsync(BackendSettings settings, string label)
        {
            var created = Newtonsoft.Json.Linq.JObject.Parse(await PostAsync(settings, "tag", new { label }));
            return new NamedItem((int)created["id"], (string)created["label"]);
        }

        public async Task<List<ReleaseProfile>> GetReleaseProfilesAsync(BackendSettings settings) =>
            ((await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "releaseprofile")) ?? new List<Newtonsoft.Json.Linq.JObject>())
                .Select(p => new ReleaseProfile
                {
                    Id = (int)p["id"],
                    Name = (string)p["name"],
                    Enabled = (bool?)p["enabled"] ?? true,
                    Required = Terms(p["required"]),
                    Ignored = Terms(p["ignored"]),
                    IndexerId = (int?)p["indexerId"] ?? 0,
                    Tags = p["tags"]?.Select(t => (int)t).ToList() ?? new List<int>()
                }).ToList();

        public async Task<ReleaseProfile> SaveReleaseProfileAsync(BackendSettings settings, ReleaseProfile profile)
        {
            var body = new { id = profile.Id, name = profile.Name, enabled = profile.Enabled, required = profile.Required, ignored = profile.Ignored, indexerId = profile.IndexerId, tags = profile.Tags };
            var saved = Newtonsoft.Json.Linq.JObject.Parse(profile.Id > 0 ? await PutAsync(settings, $"releaseprofile/{profile.Id}", body) : await PostAsync(settings, "releaseprofile", body));

            profile.Id = (int)saved["id"];
            return profile;
        }

        public async Task SetSeriesTagsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> tags)
        {
            var series = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, $"series/{seriesId}");
            series["tags"] = new Newtonsoft.Json.Linq.JArray(tags);
            await PutAsync(settings, $"series/{seriesId}", series);
        }

        // Sonarr 4 returns terms as a list; older payloads used one comma-separated string.
        private static List<string> Terms(Newtonsoft.Json.Linq.JToken token) => token?.Type switch
        {
            Newtonsoft.Json.Linq.JTokenType.Array => token.Select(t => (string)t).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            Newtonsoft.Json.Linq.JTokenType.String => ((string)token).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            _ => new List<string>()
        };

        private static MediaLookupResult Map(Newtonsoft.Json.Linq.JObject json)
        {
            var images = json["images"]?.ToObject<List<ImageResource>>();
            var id = (int?)json["id"];
            var numbers = json["seasons"]?.Select(s => (int)s["seasonNumber"]).ToList() ?? new List<int>();
            var seasons = numbers.Where(n => n > 0).OrderBy(n => n).ToList();
            var tmdbId = (int?)json["tmdbId"];

            return new MediaLookupResult
            {
                Title = (string)json["title"],
                Year = (int?)json["year"] is > 0 ? (int?)json["year"] : null,
                Overview = (string)json["overview"],
                PosterUrl = Poster(images, (string)json["remotePoster"]),
                TvdbId = (int?)json["tvdbId"],
                TmdbId = tmdbId is > 0 ? tmdbId : null,
                ImdbId = Text(json["imdbId"]),
                LibraryId = id is > 0 ? id : null,
                Seasons = seasons,
                HasSpecials = numbers.Contains(0),
                Continuing = string.Equals((string)json["status"], "continuing", StringComparison.OrdinalIgnoreCase),
                Genres = Genres(json),
                Certification = Text(json["certification"]),
                Runtime = (int?)json["runtime"] is > 0 ? (int?)json["runtime"] : null,
                Network = Text(json["network"]),
                Status = Text(json["status"]),
                Ratings = (double?)json["ratings"]?["value"] is > 0 ? new List<(string, double)> { ("Rating", (double)json["ratings"]["value"]) } : new List<(string, double)>(),
                Votes = (long?)json["ratings"]?["votes"] is > 0 ? (long?)json["ratings"]["votes"] : null,
                FirstAired = Date(json["firstAired"]),
                LastAired = Date(json["lastAired"]),
                EpisodeCount = (int?)json["statistics"]?["totalEpisodeCount"] is > 0 ? (int?)json["statistics"]["totalEpisodeCount"] : null,
                Raw = json.ToString(Newtonsoft.Json.Formatting.None)
            };
        }
    }
}
