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

    // One entry in Sonarr's / Radarr's download queue for a title. SeasonNumber is TV only.
    public record QueueEntry(int? SeasonNumber, string Title, string Status, string TrackedStatus, string TrackedState, long Size, long SizeLeft, string Message);

    // One episode as Sonarr keeps it.
    public record EpisodeInfo(int SeasonNumber, int EpisodeNumber, DateTime? AirDateUtc, bool HasFile, bool Monitored);

    // A title in an instance's library, for removals (search it, and see what is on disk).
    // TV: Seasons are the show's seasons, SeasonsWithFiles those with at least one episode file.
    public record LibraryTitle(int Id, string Title, int? Year, int? TmdbId, int? TvdbId, bool HasFile, IReadOnlyList<int> Seasons, IReadOnlyList<int> SeasonsWithFiles, string PosterUrl);

    // What EnsureWebhookAsync did to Requestarr's webhook connection on an instance.
    public enum WebhookChange
    {
        Unchanged,
        Created,
        Updated
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

        // The download queue for one library item (movie or series).
        Task<List<QueueEntry>> GetQueueAsync(BackendSettings settings, int itemId);

        // Requestarr's own webhook connection (found by its name only; the owner's connections are
        // never changed). Ensure creates or updates it; Test asks the instance to call it and
        // returns the instance's error, or null when the call got through.
        Task<WebhookChange> EnsureWebhookAsync(BackendSettings settings, string url);
        Task<bool> RemoveWebhookAsync(BackendSettings settings);
        Task<string> TestWebhookAsync(BackendSettings settings);

        // Stops (or resumes) searching for a library item. Nothing is deleted.
        Task SetMonitoredAsync(BackendSettings settings, int itemId, bool monitored);

        // Asks the instance to search for a library item's missing files now.
        Task SearchAsync(BackendSettings settings, int itemId);

        // Every title in the instance's library.
        Task<List<LibraryTitle>> GetLibraryAsync(BackendSettings settings);

        // Removes a title and its files. Never adds it to the exclusion list, so it can be requested again.
        Task DeleteItemAsync(BackendSettings settings, int itemId);
    }

    public interface IRadarrApi : IBackendApi
    {
        Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term);
        Task<MediaLookupResult> LookupTmdbAsync(BackendSettings settings, int tmdbId);
        Task<int> AddMovieAsync(BackendSettings settings, MediaLookupResult movie, int qualityProfileId, string rootFolder);

        // Whether the library movie has a file; null when it is no longer in the library.
        Task<bool?> MovieHasFileAsync(BackendSettings settings, int movieId);
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

        // Every episode of a library series; null when it is no longer in the library.
        Task<List<EpisodeInfo>> GetEpisodesAsync(BackendSettings settings, int seriesId);

        // Monitors more seasons of a library series (and optionally new ones as they air), then
        // searches for each added season. Seasons already monitored are left as they are.
        Task AddSeasonsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> seasons, bool monitorFuture);

        // Deletes those seasons' episode files and unmonitors the seasons; the show stays.
        Task DeleteSeasonsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> seasons);
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

        public const string WebhookName = "Requestarr (availability)";

        // The events that can change a request: grabbed, imported, upgraded, and a download that
        // needs a person ("manual interaction required"). Flags an instance's version lacks are skipped.
        private static readonly string[] WebhookEvents = { "onGrab", "onDownload", "onUpgrade", "onImportComplete", "onManualInteractionRequired" };

        protected abstract string ItemResource { get; }

        protected abstract string QueueItemParameter { get; }

        public async Task<List<QueueEntry>> GetQueueAsync(BackendSettings settings, int itemId)
        {
            var records = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "queue/details", (QueueItemParameter, itemId), ("includeEpisode", true));

            return (records ?? new List<Newtonsoft.Json.Linq.JObject>()).Select(r => new QueueEntry(
                (int?)r["seasonNumber"] ?? (int?)r["episode"]?["seasonNumber"],
                (string)r["title"],
                Text(r["status"]),
                Text(r["trackedDownloadStatus"]),
                Text(r["trackedDownloadState"]),
                (long?)r["size"] ?? 0,
                (long?)r["sizeleft"] ?? 0,
                QueueMessage(r))).ToList();
        }

        public async Task<WebhookChange> EnsureWebhookAsync(BackendSettings settings, string url)
        {
            var ours = await FindWebhookAsync(settings);
            var body = ours ?? (await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "notification/schema"))?.FirstOrDefault(n => (string)n["implementation"] == "Webhook")
                       ?? throw new BackendException($"{settings.Url}: no Webhook connection type");
            var changed = ours == null;

            body["name"] = WebhookName;
            changed |= SetField(body, "url", url);
            changed |= SetField(body, "method", 1);

            foreach (var flag in body.Properties().Where(p => p.Name.StartsWith("on") && p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Boolean).ToList())
            {
                var wanted = WebhookEvents.Contains(flag.Name);

                if ((bool)flag.Value != wanted)
                {
                    body[flag.Name] = wanted;
                    changed = true;
                }
            }

            if (!changed)
            {
                return WebhookChange.Unchanged;
            }

            // forceSave: keep it even while the instance can't reach Requestarr (Test reports that).
            if (ours == null)
            {
                body.Remove("id");
                body["tags"] = new Newtonsoft.Json.Linq.JArray();
                await PostAsync(settings, "notification", body, ("forceSave", true));
                return WebhookChange.Created;
            }

            await PutAsync(settings, $"notification/{(int)ours["id"]}", body, ("forceSave", true));
            return WebhookChange.Updated;
        }

        public async Task<bool> RemoveWebhookAsync(BackendSettings settings)
        {
            var ours = await FindWebhookAsync(settings);

            if (ours == null)
            {
                return false;
            }

            await DeleteAsync(settings, $"notification/{(int)ours["id"]}");
            return true;
        }

        public async Task<string> TestWebhookAsync(BackendSettings settings)
        {
            var ours = await FindWebhookAsync(settings) ?? throw new BackendException($"{settings.Url}: Requestarr's webhook connection is missing");

            try
            {
                await PostAsync(settings, "notification/test", ours);
                return null;
            }
            catch (BackendException ex)
            {
                return ex.Message;
            }
        }

        public async Task SetMonitoredAsync(BackendSettings settings, int itemId, bool monitored)
        {
            var item = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, $"{ItemResource}/{itemId}");
            item["monitored"] = monitored;
            await PutAsync(settings, $"{ItemResource}/{itemId}", item);
        }

        public Task SearchAsync(BackendSettings settings, int itemId) => PostAsync(settings, "command", SearchCommand(itemId));

        protected abstract object SearchCommand(int itemId);

        public async Task<List<LibraryTitle>> GetLibraryAsync(BackendSettings settings) =>
            (await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, ItemResource) ?? new List<Newtonsoft.Json.Linq.JObject>()).Select(LibraryTitleOf).ToList();

        // Radarr: addImportExclusion, Sonarr: addImportListExclusion; both false.
        public Task DeleteItemAsync(BackendSettings settings, int itemId) =>
            SendAsync(settings, $"{ItemResource}/{itemId}", null, System.Net.Http.HttpMethod.Delete, ("deleteFiles", "true"), (ExclusionParameter, "false"));

        protected abstract string ExclusionParameter { get; }

        protected abstract LibraryTitle LibraryTitleOf(Newtonsoft.Json.Linq.JObject item);

        protected static string PosterOf(Newtonsoft.Json.Linq.JObject item) =>
            Poster(item["images"]?.ToObject<List<ImageResource>>(), (string)item["remotePoster"]);

        private async Task<Newtonsoft.Json.Linq.JObject> FindWebhookAsync(BackendSettings settings) =>
            (await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "notification"))?.FirstOrDefault(n => (string)n["name"] == WebhookName && (string)n["implementation"] == "Webhook");

        private static string QueueMessage(Newtonsoft.Json.Linq.JObject record)
        {
            var messages = record["statusMessages"]?.SelectMany(m => m["messages"]?.Select(x => (string)x) ?? Enumerable.Empty<string>());

            return Text(record["errorMessage"]) ?? messages?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? Text(record["statusMessages"]?.FirstOrDefault()?["title"]);
        }

        private static bool SetField(Newtonsoft.Json.Linq.JObject body, string name, object value)
        {
            if (body["fields"]?.FirstOrDefault(f => (string)f["name"] == name) is not Newtonsoft.Json.Linq.JObject field)
            {
                return false;
            }

            var token = Newtonsoft.Json.Linq.JToken.FromObject(value);

            if (Newtonsoft.Json.Linq.JToken.DeepEquals(field["value"], token))
            {
                return false;
            }

            field["value"] = token;
            return true;
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

        protected Task<string> PostAsync(BackendSettings settings, string resource, object body, params (string Key, object Value)[] query) => SendAsync(settings, resource, body, System.Net.Http.HttpMethod.Post, query);

        protected Task<string> PutAsync(BackendSettings settings, string resource, object body, params (string Key, object Value)[] query) => SendAsync(settings, resource, body, System.Net.Http.HttpMethod.Put, query);

        protected Task<string> DeleteAsync(BackendSettings settings, string resource) => SendAsync(settings, resource, null, System.Net.Http.HttpMethod.Delete);

        protected async Task<string> SendAsync(BackendSettings settings, string resource, object body, System.Net.Http.HttpMethod method, params (string Key, object Value)[] query)
        {
            var builder = Builder(settings, resource);

            foreach (var (key, value) in query)
            {
                builder.AddQueryParam(key, value);
            }

            var request = builder.Post().Build();
            request.Method = method;

            if (body != null)
            {
                request.Headers.ContentType = "application/json";
                request.SetContent(Json.ToJson(body));
            }

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

        protected static DateTime? Date(Newtonsoft.Json.Linq.JToken token)
        {
            if (token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Date)
            {
                return ((DateTime)token).ToUniversalTime();
            }

            return DateTime.TryParse(Text(token), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        }

        protected static List<string> Genres(Newtonsoft.Json.Linq.JObject json) =>
            json["genres"]?.Select(g => (string)g).Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? new List<string>();

        protected static string Poster(List<ImageResource> images, string remotePoster = null)
        {
            return remotePoster ?? images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;
        }

        private class StatusResource
        {
            [Newtonsoft.Json.JsonProperty]
            public string AppName { get; set; }

            [Newtonsoft.Json.JsonProperty]
            public string Version { get; set; }
        }

        private class RootFolderResource
        {
            [Newtonsoft.Json.JsonProperty]
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

        protected override string ItemResource => "movie";

        protected override string QueueItemParameter => "movieId";

        protected override object SearchCommand(int itemId) => new { name = "MoviesSearch", movieIds = new[] { itemId } };

        protected override string ExclusionParameter => "addImportExclusion";

        protected override LibraryTitle LibraryTitleOf(Newtonsoft.Json.Linq.JObject movie)
        {
            var year = (int?)movie["year"] is > 0 ? (int?)movie["year"] : null;
            return new LibraryTitle((int)movie["id"], (string)movie["title"], year, (int?)movie["tmdbId"], null, (bool?)movie["hasFile"] ?? false, Array.Empty<int>(), Array.Empty<int>(), PosterOf(movie));
        }

        public async Task<bool?> MovieHasFileAsync(BackendSettings settings, int movieId)
        {
            try
            {
                var movie = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, $"movie/{movieId}");
                return movie == null || (int?)movie["id"] is null or 0 ? null : (bool?)movie["hasFile"] ?? false;
            }
            catch (BackendException ex) when (ex.InnerException is HttpException { Response.StatusCode: System.Net.HttpStatusCode.NotFound })
            {
                return null;
            }
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

        protected override string ItemResource => "series";

        protected override string QueueItemParameter => "seriesId";

        protected override object SearchCommand(int itemId) => new { name = "SeriesSearch", seriesId = itemId };

        protected override string ExclusionParameter => "addImportListExclusion";

        protected override LibraryTitle LibraryTitleOf(Newtonsoft.Json.Linq.JObject series)
        {
            var seasons = series["seasons"]?.OfType<Newtonsoft.Json.Linq.JObject>().ToList() ?? new List<Newtonsoft.Json.Linq.JObject>();
            var withFiles = seasons.Where(s => (int?)s["statistics"]?["episodeFileCount"] is > 0).Select(s => (int)s["seasonNumber"]).ToList();

            var year = (int?)series["year"] is > 0 ? (int?)series["year"] : null;
            var numbers = seasons.Select(s => (int)s["seasonNumber"]).Where(n => n > 0).ToList();
            return new LibraryTitle((int)series["id"], (string)series["title"], year, (int?)series["tmdbId"], (int?)series["tvdbId"], withFiles.Any(), numbers, withFiles, PosterOf(series));
        }

        public async Task DeleteSeasonsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> seasons)
        {
            var files = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "episodefile", ("seriesId", seriesId)) ?? new List<Newtonsoft.Json.Linq.JObject>();
            var doomed = files.Where(f => seasons.Contains((int)f["seasonNumber"])).Select(f => (int)f["id"]).ToList();

            if (doomed.Any())
            {
                await SendAsync(settings, "episodefile/bulk", new { episodeFileIds = doomed }, System.Net.Http.HttpMethod.Delete);
            }

            // Unmonitor the seasons and their episodes, so nothing is downloaded again.
            var series = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, $"series/{seriesId}");

            foreach (var season in series["seasons"]?.OfType<Newtonsoft.Json.Linq.JObject>() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JObject>())
            {
                if (seasons.Contains((int)season["seasonNumber"]))
                {
                    season["monitored"] = false;
                }
            }

            await PutAsync(settings, $"series/{seriesId}", series);

            var episodes = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "episode", ("seriesId", seriesId)) ?? new List<Newtonsoft.Json.Linq.JObject>();
            var unmonitor = episodes.Where(e => seasons.Contains((int)e["seasonNumber"])).Select(e => (int)e["id"]).ToList();

            if (unmonitor.Any())
            {
                await PutAsync(settings, "episode/monitor", new { episodeIds = unmonitor, monitored = false });
            }
        }

        public async Task<List<EpisodeInfo>> GetEpisodesAsync(BackendSettings settings, int seriesId)
        {
            try
            {
                var episodes = await GetAsync<List<Newtonsoft.Json.Linq.JObject>>(settings, "episode", ("seriesId", seriesId));

                return (episodes ?? new List<Newtonsoft.Json.Linq.JObject>())
                    .Select(e => new EpisodeInfo((int)e["seasonNumber"], (int)e["episodeNumber"], Date(e["airDateUtc"]), (bool?)e["hasFile"] ?? false, (bool?)e["monitored"] ?? false))
                    .ToList();
            }
            catch (BackendException ex) when (ex.InnerException is HttpException { Response.StatusCode: System.Net.HttpStatusCode.NotFound })
            {
                return null;
            }
        }

        public async Task AddSeasonsAsync(BackendSettings settings, int seriesId, IReadOnlyCollection<int> seasons, bool monitorFuture)
        {
            var series = await GetAsync<Newtonsoft.Json.Linq.JObject>(settings, $"series/{seriesId}");
            var added = new List<int>();

            foreach (var season in series["seasons"]?.OfType<Newtonsoft.Json.Linq.JObject>() ?? Enumerable.Empty<Newtonsoft.Json.Linq.JObject>())
            {
                if (seasons.Contains((int)season["seasonNumber"]) && !((bool?)season["monitored"] ?? false))
                {
                    season["monitored"] = true;
                    added.Add((int)season["seasonNumber"]);
                }
            }

            series["monitored"] = true;

            if (monitorFuture)
            {
                series["monitorNewItems"] = "all";
            }

            await PutAsync(settings, $"series/{seriesId}", series);

            foreach (var season in added)
            {
                await PostAsync(settings, "command", new { name = "SeasonSearch", seriesId, seasonNumber = season });
            }
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
