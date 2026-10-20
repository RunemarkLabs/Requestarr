using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Requestarr.Core.Anime;
using Requestarr.Core.Backends;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Gaps;

namespace Requestarr.Core.Test.AnimeDub
{
    // One Piece is anime in the fake Sonarr (genre "Anime"); Severance is not.
    public abstract class AnimeScenario : GapScenario
    {
        protected AnimeService Anime => (AnimeService)Mocker.Resolve<IAnimeService>();

        protected BackendInstance SonarrInstance => Mocker.Resolve<IBackendService>().Default(Requestarr.Core.Users.ContentType.Tv);

        [SetUp]
        public async Task AutoApproveTvForEveryone()
        {
            await SetAccess(Alice, "tv", "auto");
            await SetAccess(Bob, "tv", "auto");
        }

        protected void Priority(string priority, bool enabled = true)
        {
            var repo = Mocker.Resolve<IBackendRepository>();
            var instance = SonarrInstance;
            instance.Settings.AnimePriority = priority;
            instance.Settings.AnimeEnabled = enabled;
            repo.Update(instance);
        }

        protected async Task<AnimeTags> SetUp() => (await Anime.EnsureAsync(SonarrInstance)).Tags;

        // Picks a language on the anime card, then All seasons; returns the request message.
        protected async Task<ulong> RequestAnime(ulong user, string language, int tvdbId = OnePiece)
        {
            var message = await Ask(user, "tv", $"tvdb:{tvdbId}");
            await ClickOn(user, message, $"req-tv-lang:{tvdbId}:{language}");
            await ClickOn(user, message, $"req-tv-all:{tvdbId}:{language}");
            return message;
        }

        // A language button on an existing series (library or requested): settled at once.
        protected async Task<InteractionRun> AskLanguage(ulong user, string language, int tvdbId = OnePiece)
        {
            var message = await Ask(user, "tv", $"tvdb:{tvdbId}");
            return await ClickOn(user, message, $"req-tv-lang:{tvdbId}:{language}");
        }

        protected Request RequestFor(int tvdbId = OnePiece) => RequestStore.All().Single(r => r.TvdbId == tvdbId);
    }
}
