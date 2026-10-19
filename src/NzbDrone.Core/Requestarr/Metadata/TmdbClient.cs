using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using Requestarr.Core.Users;

namespace Requestarr.Core.Metadata
{
    // Optional The Movie Database key, from the provisioning file only. Kept in memory, never
    // written to the database or config.xml, and never logged.
    public interface ITmdbSettingsStore
    {
        string ApiKey { get; }
        bool Enabled { get; }
        void Set(string apiKey);
    }

    public class TmdbSettingsStore : ITmdbSettingsStore
    {
        public string ApiKey { get; private set; }

        public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

        public void Set(string apiKey)
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        }
    }

    public record TmdbDetails(IReadOnlyList<string> Cast, int? EpisodeCount);

    public interface ITmdbClient
    {
        Task<TmdbDetails> GetAsync(ContentType type, int tmdbId, CancellationToken cancellationToken);
    }

    // Cast (and TV episode counts) for one title per call. Uses its own HttpClient rather than the
    // Servarr one so request URLs, which may carry the key, never reach the logs.
    public class TmdbClient : ITmdbClient
    {
        public const int CastCount = 3;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly Uri BaseUri = new("https://api.themoviedb.org/3/");

        private readonly ITmdbSettingsStore _settings;
        private readonly Logger _logger;

        public TmdbClient(ITmdbSettingsStore settings, Logger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        // Swapped in tests; the live transport is the shared HttpClient.
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Transport { get; set; } = (request, token) => Http.SendAsync(request, token);

        public async Task<TmdbDetails> GetAsync(ContentType type, int tmdbId, CancellationToken cancellationToken)
        {
            var key = _settings.ApiKey;

            if (string.IsNullOrWhiteSpace(key) || tmdbId <= 0 || (type != ContentType.Movie && type != ContentType.Tv))
            {
                return null;
            }

            using var request = Build(type, tmdbId, key);

            try
            {
                using var response = await Transport(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("The Movie Database lookup for {0} {1} returned HTTP {2}", type, tmdbId, (int)response.StatusCode);
                    return null;
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var credits = type == ContentType.Movie ? json["credits"] : json["aggregate_credits"];
                var cast = credits?["cast"]?
                    .OrderBy(c => (int?)c["order"] ?? int.MaxValue)
                    .Select(c => (string)c["name"])
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Take(CastCount)
                    .ToList() ?? new List<string>();
                var episodes = type == ContentType.Tv && (int?)json["number_of_episodes"] is > 0 ? (int?)json["number_of_episodes"] : null;

                return new TmdbDetails(cast, episodes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Only the exception type: messages and URLs could carry the key.
                _logger.Warn("The Movie Database lookup for {0} {1} failed ({2})", type, tmdbId, ex.GetType().Name);
                return null;
            }
        }

        // A v4 read access token (a JWT) goes in the Authorization header; a v3 key in the query.
        public static HttpRequestMessage Build(ContentType type, int tmdbId, string key)
        {
            var path = type == ContentType.Movie ? $"movie/{tmdbId}?append_to_response=credits" : $"tv/{tmdbId}?append_to_response=aggregate_credits";
            var bearer = IsReadAccessToken(key);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, bearer ? path : $"{path}&api_key={Uri.EscapeDataString(key)}"));

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (bearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            return request;
        }

        public static bool IsReadAccessToken(string key) => key.StartsWith("eyJ", StringComparison.Ordinal) && key.Count(c => c == '.') == 2;
    }
}
