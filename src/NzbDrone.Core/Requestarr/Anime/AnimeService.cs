using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Anime
{
    public record AnimeConfig(bool Enabled, AnimePriority Priority, IReadOnlyList<string> DubRequired, IReadOnlyList<string> DubIgnored, IReadOnlyList<string> SubRequired, IReadOnlyList<string> SubIgnored);

    public record AnimeTags(int Dub, int Sub)
    {
        public int For(AnimeLanguage language) => language == AnimeLanguage.Dub ? Dub : Sub;
    }

    public record AnimeSetupResult(string Instance, int TagsCreated, int ProfilesCreated, int ProfilesUpdated, AnimeTags Tags, string Error)
    {
        public bool Ok => Error == null;
    }

    public interface IAnimeService
    {
        // Sub/dub settings for an instance; disabled for anything but an enabled Sonarr.
        AnimeConfig ConfigFor(BackendInstance instance);

        // Makes sure Requestarr's tags and release profiles exist on the instance. Idempotent; only
        // ever creates or updates items named as Requestarr's.
        Task<AnimeSetupResult> EnsureAsync(BackendInstance instance);

        // The instance's dub / sub tag ids, setting them up first when needed. Null when disabled.
        Task<AnimeTags> TagsAsync(BackendInstance instance);

        // The language a library series is steered to, from its tags; null when it has neither tag.
        AnimeLanguage? LanguageOf(IEnumerable<int> seriesTags, AnimeTags tags);

        // Steers a library series to a language: swaps Requestarr's tag, keeps every other tag.
        Task SetLanguageAsync(BackendInstance instance, int seriesId, IReadOnlyCollection<int> currentTags, AnimeLanguage language);
    }

    public class AnimeService : IAnimeService, IHandleAsync<BackendsCheckedEvent>
    {
        private readonly IBackendService _backends;
        private readonly ISonarrApi _sonarr;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<int, AnimeTags> _tags = new();

        public AnimeService(IBackendService backends, ISonarrApi sonarr, Logger logger)
        {
            _backends = backends;
            _sonarr = sonarr;
            _logger = logger;
        }

        public AnimeConfig ConfigFor(BackendInstance instance)
        {
            var settings = instance?.Settings;
            var enabled = instance != null && instance.Enable && instance.Implementation == BackendImplementations.Sonarr && settings.AnimeEnabled;

            return new AnimeConfig(
                enabled,
                AnimeDefaults.ParsePriority(settings?.AnimePriority),
                AnimeDefaults.Clean(settings?.DubRequired, AnimeDefaults.DubRequired),
                AnimeDefaults.Clean(settings?.DubIgnored, AnimeDefaults.DubIgnored),
                AnimeDefaults.Clean(settings?.SubRequired, AnimeDefaults.SubRequired),
                AnimeDefaults.Clean(settings?.SubIgnored, AnimeDefaults.SubIgnored));
        }

        public async Task<AnimeSetupResult> EnsureAsync(BackendInstance instance)
        {
            var config = ConfigFor(instance);

            if (!config.Enabled)
            {
                _tags.TryRemove(instance.Id, out _);
                return new AnimeSetupResult(instance.Name, 0, 0, 0, null, null);
            }

            try
            {
                var tagsCreated = 0;
                var existingTags = await _sonarr.GetTagsAsync(instance.Settings);

                async Task<int> Tag(string label)
                {
                    var tag = existingTags.FirstOrDefault(t => string.Equals(t.Name, label, StringComparison.OrdinalIgnoreCase));

                    if (tag != null)
                    {
                        return tag.Id;
                    }

                    tagsCreated++;
                    return (await _sonarr.CreateTagAsync(instance.Settings, label)).Id;
                }

                var tags = new AnimeTags(await Tag(AnimeDefaults.DubTag), await Tag(AnimeDefaults.SubTag));
                var profiles = await _sonarr.GetReleaseProfilesAsync(instance.Settings);
                var created = 0;
                var updated = 0;

                foreach (var (name, tag, required, ignored) in new[]
                         {
                             (AnimeDefaults.DubProfile, tags.Dub, config.DubRequired, config.DubIgnored),
                             (AnimeDefaults.SubProfile, tags.Sub, config.SubRequired, config.SubIgnored)
                         })
                {
                    var ours = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (ours == null)
                    {
                        await _sonarr.SaveReleaseProfileAsync(instance.Settings, new ReleaseProfile { Name = name, Required = required.ToList(), Ignored = ignored.ToList(), Tags = new List<int> { tag } });
                        created++;
                    }
                    else if (!ours.Enabled || !Same(ours.Required, required) || !Same(ours.Ignored, ignored) || !ours.Tags.SequenceEqual(new[] { tag }))
                    {
                        // Ours (named as Requestarr's): bring it back in line with the provisioning file.
                        ours.Enabled = true;
                        ours.Required = required.ToList();
                        ours.Ignored = ignored.ToList();
                        ours.Tags = new List<int> { tag };
                        await _sonarr.SaveReleaseProfileAsync(instance.Settings, ours);
                        updated++;
                    }
                }

                _tags[instance.Id] = tags;

                if (tagsCreated + created + updated > 0)
                {
                    _logger.Info("Anime on {0}: {1} tags and {2} release profiles created, {3} updated", instance.Name, tagsCreated, created, updated);
                }

                return new AnimeSetupResult(instance.Name, tagsCreated, created, updated, tags, null);
            }
            catch (BackendException ex)
            {
                _logger.Warn("Anime setup on {0} failed: {1}", instance.Name, ex.Message);
                return new AnimeSetupResult(instance.Name, 0, 0, 0, null, ex.Message);
            }
        }

        public async Task<AnimeTags> TagsAsync(BackendInstance instance)
        {
            if (!ConfigFor(instance).Enabled)
            {
                return null;
            }

            return _tags.TryGetValue(instance.Id, out var tags) ? tags : (await EnsureAsync(instance)).Tags;
        }

        public AnimeLanguage? LanguageOf(IEnumerable<int> seriesTags, AnimeTags tags)
        {
            if (tags == null || seriesTags == null)
            {
                return null;
            }

            var list = seriesTags.ToList();
            return list.Contains(tags.Dub) ? AnimeLanguage.Dub : list.Contains(tags.Sub) ? AnimeLanguage.Sub : null;
        }

        public async Task SetLanguageAsync(BackendInstance instance, int seriesId, IReadOnlyCollection<int> currentTags, AnimeLanguage language)
        {
            var tags = await TagsAsync(instance) ?? throw new BackendException($"sub/dub is off on {instance.Name}");
            var next = currentTags.Where(t => t != tags.Dub && t != tags.Sub).Append(tags.For(language)).ToList();

            await _sonarr.SetSeriesTagsAsync(instance.Settings, seriesId, next);
        }

        // After each backend check, set up anime on every Sonarr that passed.
        public void HandleAsync(BackendsCheckedEvent message) => SetUpAllAsync().GetAwaiter().GetResult();

        public async Task SetUpAllAsync()
        {
            foreach (var result in _backends.LastResults.Where(r => r.Ok))
            {
                var instance = _backends.Find(result.BackendId);

                if (instance?.Implementation == BackendImplementations.Sonarr)
                {
                    await EnsureAsync(instance);
                }
            }
        }

        private static bool Same(IEnumerable<string> a, IEnumerable<string> b) =>
            a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}
