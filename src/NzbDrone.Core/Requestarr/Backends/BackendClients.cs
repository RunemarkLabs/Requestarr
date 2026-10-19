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

    // A title as returned by a backend's own lookup (Radarr / Sonarr metadata; no third-party keys).
    public class MediaLookupResult
    {
        public string Title { get; set; }
        public int? Year { get; set; }
        public string Overview { get; set; }
        public string PosterUrl { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }

        // Set when the title is already in the instance's library.
        public int? LibraryId { get; set; }
        public bool HasFile { get; set; }
        public List<int> Seasons { get; set; } = new();
        public bool Continuing { get; set; }

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
        Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture);
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

        protected async Task<string> PostAsync(BackendSettings settings, string resource, object body)
        {
            var request = Builder(settings, resource).Post().Build();
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
            return result == null || (int?)result["tmdbId"] is null or 0 ? null : Map(result);
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
                LibraryId = id is > 0 ? id : null,
                HasFile = (bool?)json["hasFile"] ?? false,
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
            return (await LookupAsync(settings, $"tvdb:{tvdbId}")).FirstOrDefault(r => r.TvdbId == tvdbId);
        }

        public async Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture)
        {
            var body = Newtonsoft.Json.Linq.JObject.Parse(series.Raw);
            body["qualityProfileId"] = qualityProfileId;
            body["rootFolderPath"] = rootFolder;
            body["seriesType"] = string.IsNullOrWhiteSpace(seriesType) ? "standard" : seriesType.ToLowerInvariant();
            body["monitored"] = true;
            body["seasonFolder"] = true;
            body["monitorNewItems"] = monitorFuture ? "all" : "none";

            var seasonArray = new Newtonsoft.Json.Linq.JArray();

            foreach (var number in series.Seasons)
            {
                seasonArray.Add(Newtonsoft.Json.Linq.JObject.FromObject(new { seasonNumber = number, monitored = seasons.Contains(number) }));
            }

            body["seasons"] = seasonArray;

            // No "monitor" preset: Sonarr then keeps the per-season flags above.
            body["addOptions"] = Newtonsoft.Json.Linq.JObject.FromObject(new { searchForMissingEpisodes = true, searchForCutoffUnmetEpisodes = false, ignoreEpisodesWithFiles = true });

            var response = Newtonsoft.Json.Linq.JObject.Parse(await PostAsync(settings, "series", body));
            return (int)response["id"];
        }

        private static MediaLookupResult Map(Newtonsoft.Json.Linq.JObject json)
        {
            var images = json["images"]?.ToObject<List<ImageResource>>();
            var id = (int?)json["id"];
            var seasons = json["seasons"]?.Select(s => (int)s["seasonNumber"]).Where(n => n > 0).OrderBy(n => n).ToList() ?? new List<int>();

            return new MediaLookupResult
            {
                Title = (string)json["title"],
                Year = (int?)json["year"] is > 0 ? (int?)json["year"] : null,
                Overview = (string)json["overview"],
                PosterUrl = Poster(images, (string)json["remotePoster"]),
                TvdbId = (int?)json["tvdbId"],
                LibraryId = id is > 0 ? id : null,
                Seasons = seasons,
                Continuing = string.Equals((string)json["status"], "continuing", StringComparison.OrdinalIgnoreCase),
                Raw = json.ToString(Newtonsoft.Json.Formatting.None)
            };
        }
    }
}
