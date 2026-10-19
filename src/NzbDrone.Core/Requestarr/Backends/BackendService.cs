using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Users;

namespace Requestarr.Core.Backends
{
    public record BackendCheckResult(int BackendId, string Name, bool Ok, string Error, int? QualityProfileId);

    public class BackendsCheckedEvent : IEvent
    {
    }

    public interface IBackendService
    {
        List<BackendInstance> All();
        BackendInstance Find(int id);
        BackendInstance FindByName(string name);
        BackendInstance Default(ContentType type);
        BackendInstance ForUser(UserMediaAccess access, ContentType type);
        void SyncFromProvisioning(BackendsProvisioning backends);
        Task<BackendCheckResult> CheckAsync(BackendInstance instance);
        Task CheckAllAsync();
        IReadOnlyCollection<BackendCheckResult> LastResults { get; }
        IBackendApi ApiFor(BackendInstance instance);
    }

    public class BackendService : IBackendService, IHandleAsync<ProvisioningCompletedEvent>
    {
        public static readonly IReadOnlyDictionary<string, int> MinimumMajorVersion = new Dictionary<string, int>
        {
            { BackendImplementations.Sonarr, 4 },
            { BackendImplementations.Radarr, 5 }
        };

        private readonly IBackendRepository _repository;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<int, BackendCheckResult> _results = new();

        public BackendService(IBackendRepository repository, IRadarrApi radarr, ISonarrApi sonarr, IEventAggregator eventAggregator, Logger logger)
        {
            _repository = repository;
            _radarr = radarr;
            _sonarr = sonarr;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public IReadOnlyCollection<BackendCheckResult> LastResults => _results.Values.ToList();

        public List<BackendInstance> All() => _repository.All().ToList();

        public BackendInstance Find(int id) => _repository.Find(id);

        public BackendInstance FindByName(string name) => _repository.FindByName(name);

        public BackendInstance Default(ContentType type)
        {
            var implementation = BackendImplementations.ForContentType(type);
            var candidates = _repository.All().Where(b => b.Enable && b.Implementation == implementation).OrderBy(b => b.Id).ToList();

            return candidates.FirstOrDefault(b => b.Settings.IsDefault) ?? candidates.FirstOrDefault();
        }

        // The admin's per-user assignment wins; otherwise the type's default instance.
        public BackendInstance ForUser(UserMediaAccess access, ContentType type)
        {
            if (access?.BackendId != null)
            {
                var assigned = _repository.Find(access.BackendId.Value);

                if (assigned != null && assigned.Enable && assigned.Implementation == BackendImplementations.ForContentType(type))
                {
                    return assigned;
                }
            }

            return Default(type);
        }

        public IBackendApi ApiFor(BackendInstance instance) => instance.Implementation switch
        {
            BackendImplementations.Radarr => _radarr,
            BackendImplementations.Sonarr => _sonarr,
            _ => null
        };

        // The provisioning file is the source of truth for backends: listed instances are
        // created or updated by name; instances no longer listed are disabled (kept for history).
        public void SyncFromProvisioning(BackendsProvisioning backends)
        {
            var listed = new List<(string Implementation, BackendInstanceProvisioning Instance)>();
            listed.AddRange((backends?.Sonarr ?? new List<BackendInstanceProvisioning>()).Select(i => (BackendImplementations.Sonarr, i)));
            listed.AddRange((backends?.Radarr ?? new List<BackendInstanceProvisioning>()).Select(i => (BackendImplementations.Radarr, i)));

            var existing = _repository.All().ToList();

            foreach (var (implementation, entry) in listed)
            {
                var instance = existing.FirstOrDefault(b => string.Equals(b.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) ??
                               new BackendInstance { Name = entry.Name, Added = DateTime.UtcNow };

                instance.Implementation = implementation;
                instance.Enable = entry.Enabled;
                instance.Settings = new BackendSettings
                {
                    Url = entry.Url,
                    ApiKey = entry.ApiKey,
                    QualityProfile = entry.QualityProfile,
                    RootFolder = entry.RootFolder,
                    SeriesType = entry.SeriesType,
                    IsDefault = entry.Default,
                    PollingOnly = entry.PollingOnly
                };

                _repository.Upsert(instance);
            }

            foreach (var stale in existing.Where(b => b.Implementation != BackendImplementations.Lidarr &&
                                                      !listed.Any(l => string.Equals(l.Instance.Name, b.Name, StringComparison.OrdinalIgnoreCase)) && b.Enable))
            {
                stale.Enable = false;
                _repository.Update(stale);
                _logger.Info("Backend '{0}' is no longer in the provisioning file; disabled", stale.Name);
            }
        }

        public void HandleAsync(ProvisioningCompletedEvent message)
        {
            CheckAllAsync().GetAwaiter().GetResult();
        }

        public async Task CheckAllAsync()
        {
            _results.Clear();

            foreach (var instance in _repository.All().Where(b => b.Enable && ApiFor(b) != null))
            {
                await CheckAsync(instance);
            }

            _eventAggregator.PublishEvent(new BackendsCheckedEvent());
        }

        // Connection, version, quality profile, root folder, and (Sonarr) series type.
        public async Task<BackendCheckResult> CheckAsync(BackendInstance instance)
        {
            BackendCheckResult result;

            try
            {
                var api = ApiFor(instance);
                var status = await api.GetStatusAsync(instance.Settings);
                var minimum = MinimumMajorVersion[instance.Implementation];

                if (!string.Equals(status.AppName, instance.Implementation, StringComparison.OrdinalIgnoreCase))
                {
                    result = Fail(instance, $"{instance.Settings.Url} is {status.AppName}, not {instance.Implementation}");
                }
                else if (status.Version.Major < minimum)
                {
                    result = Fail(instance, $"{instance.Implementation} {status.Version} is not supported; version {minimum} or later is required");
                }
                else
                {
                    var profiles = await api.GetQualityProfilesAsync(instance.Settings);
                    var profile = ResolveProfile(profiles, instance.Settings.QualityProfile);
                    var roots = await api.GetRootFoldersAsync(instance.Settings);
                    var root = instance.Settings.RootFolder ?? roots.FirstOrDefault();

                    if (profile == null)
                    {
                        result = Fail(instance, $"quality profile '{instance.Settings.QualityProfile}' not found (available: {string.Join(", ", profiles.Select(p => p.Name))})");
                    }
                    else if (root == null || !roots.Any(r => SamePath(r, root)))
                    {
                        result = Fail(instance, $"root folder '{root}' not found (available: {string.Join(", ", roots)})");
                    }
                    else
                    {
                        result = new BackendCheckResult(instance.Id, instance.Name, true, null, profile.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                result = Fail(instance, ex.Message);
            }

            if (!result.Ok)
            {
                _logger.Warn("Backend '{0}' check failed: {1}", instance.Name, result.Error);
            }

            _results[instance.Id] = result;
            return result;
        }

        public static NamedItem ResolveProfile(IReadOnlyList<NamedItem> profiles, string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted))
            {
                return profiles.Count > 0 ? profiles[0] : null;
            }

            return profiles.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)) ??
                   (int.TryParse(wanted, out var id) ? profiles.FirstOrDefault(p => p.Id == id) : null);
        }

        private static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

        private static BackendCheckResult Fail(BackendInstance instance, string error) => new(instance.Id, instance.Name, false, error, null);
    }

    [CheckOn(typeof(BackendsCheckedEvent))]
    public class BackendHealthCheck : HealthCheckBase
    {
        private readonly IBackendService _backends;

        public BackendHealthCheck(IBackendService backends, ILocalizationService localizationService)
            : base(localizationService)
        {
            _backends = backends;
        }

        public override HealthCheck Check()
        {
            var failed = _backends.LastResults.Where(r => !r.Ok).ToList();

            if (!failed.Any())
            {
                return new HealthCheck(GetType());
            }

            var message = string.Join("; ", failed.Select(f => $"{f.Name}: {f.Error}"));

            return new HealthCheck(GetType(), HealthCheckResult.Error, HealthCheckReason.BackendUnavailable, $"Backend check failed. {message}", "#backend-unavailable");
        }
    }
}
