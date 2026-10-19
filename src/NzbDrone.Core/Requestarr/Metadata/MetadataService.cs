using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Metadata
{
    public interface IMetadataService
    {
        // True when cast comes from The Movie Database, so its attribution must be shown.
        bool CastEnabled { get; }

        // Adds cast (and TV episode counts) to lookup results. Never throws; a title The Movie
        // Database can't answer in time just has no cast.
        Task EnrichAsync(ContentType type, IReadOnlyCollection<MediaLookupResult> items);
    }

    public class MetadataService : IMetadataService
    {
        public static readonly TimeSpan Budget = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

        private readonly ITmdbSettingsStore _settings;
        private readonly ITmdbClient _tmdb;
        private readonly ConcurrentDictionary<(ContentType, int), (DateTime Fetched, TmdbDetails Details)> _cache = new();

        public MetadataService(ITmdbSettingsStore settings, ITmdbClient tmdb)
        {
            _settings = settings;
            _tmdb = tmdb;
        }

        public bool CastEnabled => _settings.Enabled;

        public async Task EnrichAsync(ContentType type, IReadOnlyCollection<MediaLookupResult> items)
        {
            if (!CastEnabled || items == null)
            {
                return;
            }

            using var budget = new CancellationTokenSource(Budget);
            var work = items.Where(i => i.TmdbId is > 0).Select(i => Fill(type, i, budget.Token)).ToList();

            try
            {
                await Task.WhenAll(work).WaitAsync(Budget);
            }
            catch (TimeoutException)
            {
                // Whatever arrived in time is used.
            }
        }

        private async Task Fill(ContentType type, MediaLookupResult item, CancellationToken token)
        {
            var key = (type, item.TmdbId.Value);

            if (!_cache.TryGetValue(key, out var cached) || DateTime.UtcNow - cached.Fetched > CacheFor)
            {
                var details = await _tmdb.GetAsync(type, item.TmdbId.Value, token);

                if (details == null)
                {
                    return;
                }

                cached = (DateTime.UtcNow, details);
                _cache[key] = cached;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            item.Cast = cached.Details.Cast.ToList();
            item.EpisodeCount ??= cached.Details.EpisodeCount;
        }
    }
}
