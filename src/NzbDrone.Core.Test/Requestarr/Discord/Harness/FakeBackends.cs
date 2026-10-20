using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Requestarr.Core.Backends;
using Requestarr.Core.Metadata;
using Requestarr.Core.Users;

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
            ImdbId = item.ImdbId,
            LibraryId = item.LibraryId,
            Seasons = item.Seasons.ToList(),
            HasSpecials = item.HasSpecials,
            Continuing = item.Continuing,
            Genres = item.Genres.ToList(),
            Certification = item.Certification,
            Runtime = item.Runtime,
            Studio = item.Studio,
            Network = item.Network,
            Status = item.Status,
            Ratings = item.Ratings.ToList(),
            Votes = item.Votes,
            FirstAired = item.FirstAired,
            LastAired = item.LastAired,
            InCinemas = item.InCinemas,
            DigitalRelease = item.DigitalRelease,
            PhysicalRelease = item.PhysicalRelease,
            EpisodeCount = item.EpisodeCount,
            Raw = "{}"
        };
    }

    public class FakeRadarr : FakeArr, IRadarrApi
    {
        public FakeRadarr()
        {
            AppName = "Radarr";
            Version = new Version(5, 20, 0);
            Catalog.Add(new MediaLookupResult
            {
                Title = "Dune", Year = 2021, Overview = "Paul Atreides...", TmdbId = 438631, ImdbId = "tt1160419", PosterUrl = "https://image.example/dune.jpg",
                Genres = new() { "Science Fiction", "Adventure" }, Certification = "PG-13", Runtime = 155, Studio = "Legendary Pictures", Status = "released",
                Ratings = new() { ("IMDb", 8.0), ("TMDB", 7.785), ("Rotten Tomatoes", 83) },
                InCinemas = new DateTime(2021, 9, 15, 0, 0, 0, DateTimeKind.Utc), DigitalRelease = new DateTime(2021, 10, 21, 0, 0, 0, DateTimeKind.Utc), PhysicalRelease = new DateTime(2021, 12, 22, 0, 0, 0, DateTimeKind.Utc)
            });
            Catalog.Add(new MediaLookupResult { Title = "Dune: Part Two", Year = 2024, Overview = "Paul joins the Fremen.", TmdbId = 693134 });
            Catalog.Add(new MediaLookupResult { Title = "Arrival", Year = 2016, Overview = "Linguist.", TmdbId = 329865, LibraryId = 7, HasFile = true });
        }

        public Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term) =>
            Run(() => Catalog.Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(Copy).ToList());

        public Task<MediaLookupResult> LookupTmdbAsync(BackendSettings settings, int tmdbId) => Run(() => Copy(Catalog.FirstOrDefault(c => c.TmdbId == tmdbId)));

        public Task<int> AddMovieAsync(BackendSettings settings, MediaLookupResult movie, int qualityProfileId, string rootFolder) =>
            Run(() => Add(settings, movie, qualityProfileId, rootFolder, null, null, false));
    }

    // The Movie Database stand-in: cast per (type, TMDB ID), and a record of calls.
    public class FakeTmdb : ITmdbClient
    {
        public Dictionary<(ContentType Type, int Id), TmdbDetails> Titles { get; } = new()
        {
            [(ContentType.Tv, 196322)] = new TmdbDetails(new[] { "Joel Edgerton", "Jennifer Connelly", "Alice Braga" }, 9),
            [(ContentType.Tv, 62425)] = new TmdbDetails(new[] { "Marc Bendavid", "Melissa O'Neil", "Anthony Lemke" }, 39),
            [(ContentType.Movie, 438631)] = new TmdbDetails(new[] { "Timothée Chalamet", "Rebecca Ferguson", "Oscar Isaac" }, null)
        };

        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public Task<TmdbDetails> GetAsync(ContentType type, int tmdbId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Fail ? null : Titles.GetValueOrDefault((type, tmdbId)));
        }
    }

    public class FakeSonarr : FakeArr, ISonarrApi
    {
        public FakeSonarr()
        {
            AppName = "Sonarr";
            Version = new Version(4, 0, 20);
            Catalog.Add(new MediaLookupResult { Title = "Severance", Year = 2022, Overview = "Work-life balance.", TvdbId = 371980, Seasons = new() { 1, 2 }, Continuing = true });
            Catalog.Add(new MediaLookupResult { Title = "The Long Show", Year = 1990, Overview = "Many seasons.", TvdbId = 100000, Seasons = Enumerable.Range(1, 40).ToList() });

            // A 36-season show and a 1,000-plus-episode anime, for paging through Choose seasons.
            Catalog.Add(new MediaLookupResult { Title = "Thirty Six", Year = 1989, Overview = "Runs forever.", TvdbId = 500036, Seasons = Enumerable.Range(1, 36).ToList(), HasSpecials = true, Continuing = true });
            Catalog.Add(new MediaLookupResult
            {
                Title = "One Piece", Year = 1999, Overview = "Pirates.", TvdbId = 81797, Seasons = Enumerable.Range(1, 22).ToList(), HasSpecials = true, Continuing = true,
                Genres = new() { "Animation", "Anime" }, EpisodeCount = 1122
            });

            // Two shows with the same name; Sonarr already puts the year in the newer one's title.
            Catalog.Add(new MediaLookupResult
            {
                Title = "Dark Matter (2024)", Year = 2024, Overview = "A physicist is abducted into an alternate version of his life.", TvdbId = 425409, TmdbId = 196322, ImdbId = "tt14866304",
                PosterUrl = "https://image.example/dark-matter-2024.jpg", Seasons = new() { 1, 2 }, HasSpecials = true, Continuing = true, Network = "Apple TV",
                Genres = new() { "Drama", "Mystery", "Science Fiction" }, Certification = "TV-MA", Runtime = 54, Status = "continuing",
                Ratings = new() { ("Rating", 7.6) }, Votes = 83949, FirstAired = new DateTime(2024, 5, 8, 0, 0, 0, DateTimeKind.Utc)
            });
            Catalog.Add(new MediaLookupResult
            {
                Title = "Dark Matter", Year = 2015, Overview = "Six people wake up on a derelict spaceship.", TvdbId = 292174, TmdbId = 62425, Seasons = new() { 1, 2, 3 },
                Network = "SYFY", Genres = new() { "Adventure", "Drama" }, Status = "ended", FirstAired = new DateTime(2015, 6, 12, 0, 0, 0, DateTimeKind.Utc), LastAired = new DateTime(2017, 8, 25, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        public Task<List<MediaLookupResult>> LookupAsync(BackendSettings settings, string term) =>
            Run(() => Catalog.Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(Copy).ToList());

        public Task<MediaLookupResult> LookupTvdbAsync(BackendSettings settings, int tvdbId) => Run(() => Copy(Catalog.FirstOrDefault(c => c.TvdbId == tvdbId)));

        public Task<int> AddSeriesAsync(BackendSettings settings, MediaLookupResult series, int qualityProfileId, string rootFolder, string seriesType, IReadOnlyCollection<int> seasons, bool monitorFuture) =>
            Run(() => Add(settings, series, qualityProfileId, rootFolder, seriesType, seasons, monitorFuture));
    }
}
