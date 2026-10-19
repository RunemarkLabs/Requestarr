using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Backends;

namespace Requestarr.Core.Test.Discord.Harness
{
    // In-memory Radarr / Sonarr: a lookup catalog, a library, profiles and root folders, and
    // a record of what was added, per instance URL.
    public abstract class FakeArr : IBackendApi
    {
        public record Added(string Url, MediaLookupResult Item, int QualityProfileId, string RootFolder, string SeriesType, IReadOnlyCollection<int> Seasons, bool MonitorFuture);

        public string AppName { get; set; }
        public Version Version { get; set; }
        public bool Unreachable { get; set; }
        public bool FailAdds { get; set; }
        public List<NamedItem> Profiles { get; } = new() { new(1, "Any"), new(4, "HD-1080p") };
        public List<string> RootFolders { get; } = new() { "/data/media" };
        public List<MediaLookupResult> Catalog { get; } = new();
        public List<Added> Adds { get; } = new();

        public Task<BackendStatus> GetStatusAsync(BackendSettings settings) => Run(() => new BackendStatus(AppName, Version));

        public Task<List<NamedItem>> GetQualityProfilesAsync(BackendSettings settings) => Run(() => Profiles.ToList());

        public Task<List<string>> GetRootFoldersAsync(BackendSettings settings) => Run(() => RootFolders.ToList());

        protected Task<T> Run<T>(Func<T> action)
        {
            if (Unreachable)
            {
                throw new BackendException("Connection refused");
            }

            return Task.FromResult(action());
        }

        protected int Add(BackendSettings settings, MediaLookupResult item, int profile, string root, string seriesType, IReadOnlyCollection<int> seasons, bool future)
        {
            if (FailAdds)
            {
                throw new BackendException("400 BadRequest: validation failed");
            }

            Adds.Add(new Added(settings.Url, item, profile, root, seriesType, seasons?.ToList(), future));
            var libraryId = Adds.Count + 100;
            Catalog.Single(c => c == item || (c.TmdbId != null && c.TmdbId == item.TmdbId) || (c.TvdbId != null && c.TvdbId == item.TvdbId)).LibraryId = libraryId;
            return libraryId;
        }

        protected static MediaLookupResult Copy(MediaLookupResult item) => item == null ? null : new MediaLookupResult
        {
            Title = item.Title,
            Year = item.Year,
            Overview = item.Overview,
            PosterUrl = item.PosterUrl,
            TmdbId = item.TmdbId,
            TvdbId = item.TvdbId,
            LibraryId = item.LibraryId,
            Seasons = item.Seasons.ToList(),
            Continuing = item.Continuing,
            Raw = "{}"
        };
    }

    public class FakeRadarr : FakeArr, IRadarrApi
    {
        public FakeRadarr()
        {
            AppName = "Radarr";
            Version = new Version(5, 20, 0);
            Catalog.Add(new MediaLookupResult { Title = "Dune", Year = 2021, Overview = "Paul Atreides...", TmdbId = 438631, PosterUrl = "https://image.example/dune.jpg" });
            Catalog.Add(new MediaLookupResult { Title = "Dune: Part Two", Year = 2024, Overview = "Paul joins the Fremen.", TmdbId = 693134 });
            Catalog.Add(new MediaLookupResult { Title = "Arrival", Year = 2016, Overview = "Linguist.", TmdbId = 329865, LibraryId = 7, HasFile = true });
        }

        public Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term) =>
            Run(() => Catalog.Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(Copy).ToList());

        public Task<MediaLookupResult> LookupTmdbAsync(BackendSettings settings, int tmdbId) => Run(() => Copy(Catalog.FirstOrDefault(c => c.TmdbId == tmdbId)));

        public Task<int> AddMovieAsync(BackendSettings settings, MediaLookupResult movie, int qualityProfileId, string rootFolder) =>
            Run(() => Add(settings, movie, qualityProfileId, rootFolder, null, null, false));
    }

    public class FakeSonarr : FakeArr, ISonarrApi
    {
        public FakeSonarr()
        {
            AppName = "Sonarr";
            Version = new Version(4, 0, 20);
            Catalog.Add(new MediaLookupResult { Title = "Severance", Year = 2022, Overview = "Work-life balance.", TvdbId = 371980, Seasons = new() { 1, 2 }, Continuing = true });
            Catalog.Add(new MediaLookupResult { Title = "The Long Show", Year = 1990, Overview = "Many seasons.", TvdbId = 100000, Seasons = Enumerable.Range(1, 40).ToList() });
        }

        public Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term) =>
            Run(() => Catalog.Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(Copy).ToList());

        public Task<MediaLookupResult> LookupTvdbAsync(BackendSettings settings, int tvdbId) => Run(() => Copy(Catalog.FirstOrDefault(c => c.TvdbId == tvdbId)));

        public Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture) =>
            Run(() => Add(settings, series, qualityProfileId, rootFolder, seriesType, seasons, monitorFuture));
    }
}
