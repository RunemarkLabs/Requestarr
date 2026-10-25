using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Availability
{
    // What Sonarr / Radarr say about a sent request right now. Complete: everything requested is
    // there (TV: every requested season whose aired episodes all have files). SizeLeft is the
    // bytes still to download, to spot a download that stopped moving.
    public record ProgressSnapshot(bool Complete, IReadOnlyList<int> AvailableSeasons, DownloadProgress Progress, string Detail, long? SizeLeft);

    public enum WebhookOutcome
    {
        Accepted,
        Ignored,
        Unauthorized,
        UnknownInstance
    }

    // Runs every few minutes; the service decides whether the backup poll is due.
    public class AvailabilityCheckCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }

    public class AvailabilityWebhooksCheckedEvent : IEvent
    {
    }

    public interface IAvailabilityService
    {
        // A call from an instance's webhook. The payload only says which title to look at: the
        // state always comes from the instance itself, so a webhook and the poll agree.
        Task<WebhookOutcome> HandleWebhookAsync(int backendId, string token, string eventType, int? tvdbId, int? tmdbId);

        Task CheckAllAsync();
        Task CheckAsync(int requestId);

        // Creates, updates or removes Requestarr's webhook on each instance and tests it.
        Task SetUpWebhooksAsync();

        // Instance name -> why its webhook can't be used (polling still covers it).
        IReadOnlyDictionary<string, string> WebhookProblems { get; }
    }

    public class AvailabilityService : IAvailabilityService, IExecute<AvailabilityCheckCommand>, IHandleAsync<BackendsCheckedEvent>
    {
        // A download whose remaining size hasn't changed for this long counts as stuck.
        public static readonly TimeSpan StallAfter = TimeSpan.FromHours(6);

        private readonly IRequestRepository _requests;
        private readonly IBackendService _backends;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly IRequestService _requestService;
        private readonly IAvailabilitySettingsStore _settings;
        private readonly IConfigFileProvider _config;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        // One check at a time, so a webhook and the poll arriving together report a change once.
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ConcurrentDictionary<string, string> _problems = new();
        private DateTime _lastPoll = DateTime.MinValue;

        public AvailabilityService(IRequestRepository requests,
                                   IBackendService backends,
                                   IRadarrApi radarr,
                                   ISonarrApi sonarr,
                                   IRequestService requestService,
                                   IAvailabilitySettingsStore settings,
                                   IConfigFileProvider config,
                                   IDiscordGuildGateway gateway,
                                   IEventAggregator events,
                                   Logger logger)
        {
            _requests = requests;
            _backends = backends;
            _radarr = radarr;
            _sonarr = sonarr;
            _requestService = requestService;
            _settings = settings;
            _config = config;
            _gateway = gateway;
            _events = events;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public IReadOnlyDictionary<string, string> WebhookProblems => _problems;

        public async Task<WebhookOutcome> HandleWebhookAsync(int backendId, string token, string eventType, int? tvdbId, int? tmdbId)
        {
            var instance = _backends.Find(backendId);

            if (instance == null)
            {
                return WebhookOutcome.UnknownInstance;
            }

            if (!WebhookTokens.Matches(_config.ApiKey, backendId, token))
            {
                _logger.Warn("Webhook call for {0} with a wrong token was refused", instance.Name);
                return WebhookOutcome.Unauthorized;
            }

            if (string.Equals(eventType, "Test", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("Webhook test from {0} received", instance.Name);
                return WebhookOutcome.Accepted;
            }

            var (type, id) = instance.Implementation == BackendImplementations.Sonarr ? (ContentType.Tv, tvdbId) : (ContentType.Movie, tmdbId);
            var tracked = id.HasValue ? _requests.ForTitle(type, id.Value).Where(r => r.BackendId == backendId && r.IsTracked).ToList() : new List<Request>();

            if (!tracked.Any())
            {
                return WebhookOutcome.Ignored;
            }

            _logger.Debug("Webhook {0} from {1}: checking {2} request(s)", eventType.ForLog(), instance.Name, tracked.Count);

            foreach (var request in tracked)
            {
                await CheckAsync(request.Id);
            }

            return WebhookOutcome.Accepted;
        }

        public void Execute(AvailabilityCheckCommand message)
        {
            if (Clock() - _lastPoll >= TimeSpan.FromMinutes(_settings.PollMinutes))
            {
                CheckAllAsync().GetAwaiter().GetResult();
            }
        }

        public async Task CheckAllAsync()
        {
            // Without Discord nobody could be told; the next poll picks the change up.
            if (!_gateway.IsConnected)
            {
                return;
            }

            _lastPoll = Clock();

            foreach (var request in _requests.WithStatus(RequestStatus.Approved).Where(r => r.IsTracked))
            {
                await CheckAsync(request.Id);
            }
        }

        public async Task CheckAsync(int requestId)
        {
            await _lock.WaitAsync();

            try
            {
                var request = _requests.Find(requestId);
                var instance = request == null ? null : _backends.Find(request.BackendId ?? 0);

                if (request?.IsTracked != true || instance == null || !instance.Enable || !_gateway.IsConnected)
                {
                    return;
                }

                var snapshot = await EvaluateAsync(request, instance);

                if (snapshot != null)
                {
                    await _requestService.ApplyProgressAsync(request, snapshot);
                }
            }
            catch (BackendException ex)
            {
                _logger.Debug(ex, "Availability check for request {0} failed; the next poll tries again", requestId);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ProgressSnapshot> EvaluateAsync(Request request, BackendInstance instance)
        {
            var itemId = request.ExternalItemId.Value;
            var queue = await _backends.ApiFor(instance).GetQueueAsync(instance.Settings, itemId);

            if (request.ContentType == ContentType.Movie)
            {
                var hasFile = await _radarr.MovieHasFileAsync(instance.Settings, itemId);

                if (hasFile == null)
                {
                    return null;
                }

                return hasFile.Value
                    ? new ProgressSnapshot(true, Array.Empty<int>(), DownloadProgress.Imported, null, null)
                    : Summarize(request, queue, Array.Empty<int>());
            }

            var episodes = await _sonarr.GetEpisodesAsync(instance.Settings, itemId);

            if (episodes == null)
            {
                return null;
            }

            var now = Clock();
            var aired = request.Seasons
                .Select(s => (Season: s, Episodes: episodes.Where(e => e.SeasonNumber == s && e.AirDateUtc.HasValue && e.AirDateUtc <= now).ToList()))
                .Where(s => s.Episodes.Any())
                .ToList();
            var available = aired.Where(s => s.Episodes.All(e => e.HasFile)).Select(s => s.Season).OrderBy(s => s).ToList();

            // Seasons that haven't aired yet don't hold a request back; it needs something aired.
            return aired.Any() && available.Count == aired.Count
                ? new ProgressSnapshot(true, available, DownloadProgress.Imported, null, null)
                : Summarize(request, queue, available);
        }

        // Failed: the instance marks the download failed. Stuck: the instance or the download
        // client flags it (a warning or an error, for example stalled or import blocked), or its
        // remaining size hasn't moved for StallAfter. Downloading: anything else in the queue.
        private ProgressSnapshot Summarize(Request request, List<QueueEntry> queue, IReadOnlyList<int> available)
        {
            if (!queue.Any())
            {
                return new ProgressSnapshot(false, available, DownloadProgress.None, null, null);
            }

            var failed = queue.FirstOrDefault(q => Is(q.Status, "failed") || Is(q.TrackedState, "failed") || Is(q.TrackedState, "failedPending"));

            if (failed != null)
            {
                return new ProgressSnapshot(false, available, DownloadProgress.Failed, Clean(failed.Message), null);
            }

            var flagged = queue.FirstOrDefault(q => Is(q.TrackedStatus, "warning") || Is(q.TrackedStatus, "error") || Is(q.Status, "warning") || Is(q.TrackedState, "importBlocked"));

            if (flagged != null)
            {
                return new ProgressSnapshot(false, available, DownloadProgress.Stalled, Clean(flagged.Message), null);
            }

            var left = queue.Sum(q => q.SizeLeft);

            if (left > 0 && request.Progress is DownloadProgress.Downloading or DownloadProgress.Stalled && request.ProgressSizeLeft == left && request.ProgressSince <= Clock() - StallAfter)
            {
                return new ProgressSnapshot(false, available, DownloadProgress.Stalled, Requestarr.Core.Discord.DiscordText.NoProgressFor((int)StallAfter.TotalHours), left);
            }

            return new ProgressSnapshot(false, available, DownloadProgress.Downloading, null, left);
        }

        public async Task SetUpWebhooksAsync()
        {
            var baseUrl = _settings.WebhookUrl;

            foreach (var instance in _backends.All().Where(b => b.Enable && b.Implementation is BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                var api = _backends.ApiFor(instance);

                try
                {
                    if (baseUrl == null || instance.Settings.PollingOnly)
                    {
                        _problems.TryRemove(instance.Name, out _);

                        if (await api.RemoveWebhookAsync(instance.Settings))
                        {
                            _logger.Info("Removed Requestarr's webhook from {0} (polling only)", instance.Name);
                        }

                        continue;
                    }

                    var change = await api.EnsureWebhookAsync(instance.Settings, WebhookTokens.Url(baseUrl, _config.ApiKey, instance.Id));

                    if (change != WebhookChange.Unchanged)
                    {
                        _logger.Info("{0} Requestarr's webhook on {1}", change, instance.Name);
                    }

                    var error = await api.TestWebhookAsync(instance.Settings);

                    if (error == null)
                    {
                        _problems.TryRemove(instance.Name, out _);
                    }
                    else
                    {
                        _problems[instance.Name] = Requestarr.Core.Discord.DiscordText.WebhookUnreachable(baseUrl, CleanseLogMessage.Cleanse(error), _settings.PollMinutes);
                        _logger.Warn("{0} can't reach Requestarr at {1}; polling every {2} minutes covers it", instance.Name, baseUrl, _settings.PollMinutes);
                    }
                }
                catch (BackendException ex)
                {
                    _logger.Debug(ex, "Webhook setup on {0} failed", instance.Name);
                }
            }

            _events.PublishEvent(new AvailabilityWebhooksCheckedEvent());
        }

        public void HandleAsync(BackendsCheckedEvent message) => SetUpWebhooksAsync().GetAwaiter().GetResult();

        private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        private static string Clean(string message) => string.IsNullOrWhiteSpace(message) ? null : Requestarr.Core.Discord.DiscordLimits.Truncate(message.Trim(), 200);
    }

    [CheckOn(typeof(AvailabilityWebhooksCheckedEvent))]
    public class WebhookHealthCheck : HealthCheckBase
    {
        private readonly IAvailabilityService _availability;

        public WebhookHealthCheck(IAvailabilityService availability, ILocalizationService localizationService)
            : base(localizationService)
        {
            _availability = availability;
        }

        public override HealthCheck Check()
        {
            var problems = _availability.WebhookProblems;

            if (!problems.Any())
            {
                return new HealthCheck(GetType());
            }

            var message = string.Join("; ", problems.Select(p => $"{p.Key}: {p.Value}"));

            return new HealthCheck(GetType(), HealthCheckResult.Warning, HealthCheckReason.WebhookUnreachable, message, "#webhook-unreachable");
        }
    }
}
