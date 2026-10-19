using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // Who is requesting: a Requestarr user in their own channel, or an admin in the admin
    // channel (admin requests skip access levels, approval, and quotas).
    public record RequestCaller(RequestarrUser User, bool AsAdmin, Actor Actor, string Name);

    public class TitleState
    {
        public MediaLookupResult Item { get; init; }
        public BackendInstance Instance { get; init; }
        public Request Active { get; init; }
        public string ActiveOwner { get; init; }
        public bool CallerOwnsActive { get; init; }
        public bool CallerFollows { get; init; }
        public Request LastDenied { get; init; }
    }

    public record LookupOutcome<T>(T Value, string Error)
    {
        public static LookupOutcome<T> Ok(T value) => new(value, null);

        public static LookupOutcome<T> Fail(string error) => new(default, error);
    }

    public record SeasonChoice(bool All, IReadOnlyCollection<int> Seasons, bool Future);

    // A request outcome that also carries the request it created (for a Cancel button on pending ones).
    public record RequestResult(bool Success, string Message, Request Request) : ServiceResult(Success, Message);

    public interface IRequestService
    {
        Task<LookupOutcome<List<MediaLookupResult>>> SearchAsync(RequestCaller caller, ContentType type, string query);
        Task<LookupOutcome<TitleState>> DetailAsync(RequestCaller caller, ContentType type, int externalId);
        Task<ServiceResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null);
        Task<ServiceResult> ApproveAsync(int requestId, Actor admin, string adminName);
        Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName);
        ServiceResult Cancel(RequestCaller caller, int requestId);
        List<Request> List(RequestarrUser user, bool all, RequestStatus? status, ContentType? type);
        string OwnerName(Request request);
        string QuotaSummary(RequestarrUser user);
        Request Find(int id);
    }

    public class RequestService : IRequestService
    {
        public const int MaxSearchResults = 10;

        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IBackendService _backends;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public RequestService(IRequestRepository requests,
                              IRequestSubscriberRepository subscribers,
                              IRequestarrUserRepository users,
                              IUserMediaAccessRepository access,
                              IBackendService backends,
                              IRadarrApi radarr,
                              ISonarrApi sonarr,
                              IDiscordGuildGateway gateway,
                              IGuildSetupService guildSetup,
                              IAuditService audit,
                              Logger logger)
        {
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _access = access;
            _backends = backends;
            _radarr = radarr;
            _sonarr = sonarr;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _audit = audit;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public Request Find(int id) => _requests.Find(id);

        public async Task<LookupOutcome<List<MediaLookupResult>>> SearchAsync(RequestCaller caller, ContentType type, string query)
        {
            var (instance, error) = Resolve(caller, type);

            if (instance == null)
            {
                return LookupOutcome<List<MediaLookupResult>>.Fail(error);
            }

            try
            {
                var results = type == ContentType.Movie
                    ? await _radarr.LookupAsync(instance.Settings, query)
                    : await _sonarr.LookupAsync(instance.Settings, query);

                return results.Any()
                    ? LookupOutcome<List<MediaLookupResult>>.Ok(results.Where(r => r.ExternalId(type).HasValue).Take(MaxSearchResults).ToList())
                    : LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.NothingFound);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Lookup on {0} failed", instance.Name);
                return LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.BackendUnavailable(instance.Name));
            }
        }

        public async Task<LookupOutcome<TitleState>> DetailAsync(RequestCaller caller, ContentType type, int externalId)
        {
            var (instance, error) = Resolve(caller, type);

            if (instance == null)
            {
                return LookupOutcome<TitleState>.Fail(error);
            }

            MediaLookupResult item;

            try
            {
                item = await Lookup(instance, type, externalId);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Lookup on {0} failed", instance.Name);
                return LookupOutcome<TitleState>.Fail(DiscordText.BackendUnavailable(instance.Name));
            }

            if (item == null)
            {
                return LookupOutcome<TitleState>.Fail(DiscordText.TitleNotFound);
            }

            var forTitle = _requests.ForTitle(type, externalId).Where(r => r.BackendId == instance.Id).Newest().ToList();
            var active = forTitle.FirstOrDefault(r => r.IsActive);

            return LookupOutcome<TitleState>.Ok(new TitleState
            {
                Item = item,
                Instance = instance,
                Active = active,
                ActiveOwner = active == null ? null : OwnerName(active),
                CallerOwnsActive = active != null && caller.User != null && active.OwnerUserId == caller.User.Id,
                CallerFollows = active != null && caller.User != null && _subscribers.ForRequest(active.Id).Any(s => s.UserId == caller.User.Id),
                LastDenied = active == null ? forTitle.FirstOrDefault(r => r.Status == RequestStatus.Denied) : null
            });
        }

        public async Task<ServiceResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null)
        {
            if (type == ContentType.Music)
            {
                return ServiceResult.Fail(DiscordText.MusicNotYet);
            }

            var access = caller.AsAdmin ? null : AccessFor(caller.User, type);

            if (!caller.AsAdmin && access.State == AccessState.Off)
            {
                _audit.Record(caller.Actor, "request.refused", "title", $"{type}:{externalId}", "access off");
                return ServiceResult.Fail(DiscordText.AccessOff(type));
            }

            var detail = await DetailAsync(caller, type, externalId);

            if (detail.Error != null)
            {
                return ServiceResult.Fail(detail.Error);
            }

            var state = detail.Value;
            var item = state.Item;

            if (item.LibraryId.HasValue)
            {
                return ServiceResult.Fail(DiscordText.AlreadyInLibrary);
            }

            // Duplicate: attach as subscriber instead of a second request. No quota cost.
            if (state.Active != null)
            {
                if (state.CallerOwnsActive)
                {
                    return ServiceResult.Fail(DiscordText.AlreadyRequestedByYou);
                }

                if (state.CallerFollows)
                {
                    return ServiceResult.Fail(DiscordText.AlreadyFollowing);
                }

                _subscribers.Insert(new RequestSubscriber { RequestId = state.Active.Id, UserId = caller.User.Id, Added = Clock() });
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString());

                return ServiceResult.Ok(DiscordText.Subscribed(state.Active.Label, state.ActiveOwner, state.Active.Id));
            }

            var chosen = new List<int>();
            var future = false;

            if (type == ContentType.Tv)
            {
                seasons ??= new SeasonChoice(true, Array.Empty<int>(), item.Continuing);
                chosen = (seasons.All ? item.Seasons : seasons.Seasons.Where(item.Seasons.Contains)).Distinct().OrderBy(s => s).ToList();
                future = seasons.Future;

                if (!chosen.Any() && !future)
                {
                    return ServiceResult.Fail(DiscordText.ChooseSeasons);
                }
            }

            if (!caller.AsAdmin && access.QuotaLimit.HasValue)
            {
                var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
                var counted = _requests.OwnedBy(caller.User.Id)
                    .Where(r => r.ContentType == type && r.IsActive && r.Requested > Clock() - window)
                    .OrderBy(r => r.Requested)
                    .ToList();

                if (counted.Count >= access.QuotaLimit.Value)
                {
                    var nextSlot = counted[counted.Count - access.QuotaLimit.Value].Requested + window;
                    _audit.Record(caller.Actor, "request.refused", "title", $"{type}:{externalId}", $"quota {counted.Count}/{access.QuotaLimit}");
                    return ServiceResult.Fail(DiscordText.QuotaReached(type, access.QuotaLimit.Value, (int)window.TotalDays, nextSlot));
                }
            }

            var request = _requests.Insert(new Request
            {
                OwnerUserId = caller.User.Id,
                ContentType = type,
                Title = item.Title,
                Year = item.Year,
                TmdbId = type == ContentType.Movie ? externalId : null,
                TvdbId = type == ContentType.Tv ? externalId : null,
                Status = RequestStatus.Pending,
                BackendId = state.Instance.Id,
                Seasons = chosen,
                MonitorFutureSeasons = future,
                Requested = Clock()
            });

            var seasonDetail = type == ContentType.Tv ? $", seasons {DiscordText.Seasons(request)}" : string.Empty;
            _audit.Record(caller.Actor, "request.create", "request", request.Id.ToString(), $"{type} {request.Label} on {state.Instance.Name}{seasonDetail}");

            if (caller.AsAdmin || access.State == AccessState.AutoApprove)
            {
                request.DecidedByUserId = caller.AsAdmin ? caller.User.Id : null;
                var sent = await SendAsync(request, state.Instance, item, caller.AsAdmin ? caller.Actor : Actor.System);

                return sent
                    ? new RequestResult(true, DiscordText.RequestSent(request.Label, request.Id, state.Instance.Name), request)
                    : new RequestResult(false, DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason), request);
            }

            await PostApprovalCard(request, state.Instance, item, caller.Name);

            return new RequestResult(true, DiscordText.RequestPending(request.Label, request.Id), request);
        }

        public async Task<ServiceResult> ApproveAsync(int requestId, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            if (instance == null || !instance.Enable)
            {
                return ServiceResult.Fail(DiscordText.NoBackend(request.ContentType));
            }

            request.DecidedByUserId = admin.UserId;
            _audit.Record(admin, "request.approve", "request", request.Id.ToString());

            var sent = await SendAsync(request, instance, null, admin);

            await Notify(request, sent
                ? DiscordText.RequestApprovedNotice(request.Label, request.Id, instance.Name)
                : DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));

            return sent
                ? ServiceResult.Ok(DiscordText.AdminApproved(request, DiscordText.RequestSent(request.Label, request.Id, instance.Name)))
                : ServiceResult.Fail(DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));
        }

        public async Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            request.Status = RequestStatus.Denied;
            request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            request.Decided = Clock();
            request.DecidedByUserId = admin.UserId;
            _requests.Update(request);

            _audit.Record(admin, "request.deny", "request", request.Id.ToString(), request.DecisionReason);
            await Notify(request, DiscordText.RequestDeniedNotice(request.Label, request.Id, request.DecisionReason));

            return ServiceResult.Ok(DiscordText.AdminDenied(request));
        }

        public ServiceResult Cancel(RequestCaller caller, int requestId)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (caller.User == null || request.OwnerUserId != caller.User.Id)
            {
                return ServiceResult.Fail(DiscordText.NotYourRequest);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.CannotCancel(request.Status));
            }

            request.Status = RequestStatus.Cancelled;
            request.Decided = Clock();
            _requests.Update(request);
            _audit.Record(caller.Actor, "request.cancel", "request", request.Id.ToString());

            return ServiceResult.Ok(DiscordText.RequestCancelled(request.Label, request.Id));
        }

        public List<Request> List(RequestarrUser user, bool all, RequestStatus? status, ContentType? type)
        {
            IEnumerable<Request> requests;

            if (all && user == null)
            {
                requests = _requests.All();
            }
            else
            {
                var followed = _subscribers.ForUser(user.Id).Select(s => s.RequestId).ToHashSet();
                requests = _requests.OwnedBy(user.Id).Concat(_requests.Get(followed));
            }

            return requests
                .Where(r => status == null || r.Status == status)
                .Where(r => type == null || r.ContentType == type)
                .GroupBy(r => r.Id).Select(g => g.First())
                .Newest()
                .ToList();
        }

        public string OwnerName(Request request) => _users.Find(request.OwnerUserId)?.DisplayName ?? $"user {request.OwnerUserId}";

        public string QuotaSummary(RequestarrUser user)
        {
            var lines = new List<string> { $"**{user.DisplayName}**" };

            foreach (var type in ContentTypes.Offered)
            {
                var access = AccessFor(user, type);
                var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
                var counted = _requests.OwnedBy(user.Id).Where(r => r.ContentType == type && r.IsActive && r.Requested > Clock() - window).OrderBy(r => r.Requested).ToList();
                DateTime? next = access.QuotaLimit.HasValue && counted.Count >= access.QuotaLimit.Value
                    ? counted[counted.Count - access.QuotaLimit.Value].Requested + window
                    : null;

                lines.Add(DiscordText.QuotaLine(type, access.State, counted.Count, access.QuotaLimit, (int)window.TotalDays, next));
            }

            return string.Join("\n", lines);
        }

        // Adds the title to the instance: its quality profile, root folder, series type,
        // monitored, search started. Records the item ID; failures mark the request Failed.
        private async Task<bool> SendAsync(Request request, BackendInstance instance, MediaLookupResult item, Actor actor)
        {
            try
            {
                var api = _backends.ApiFor(instance);
                item ??= await Lookup(instance, request.ContentType, request.ExternalId.Value)
                         ?? throw new BackendException(DiscordText.TitleNotFound);

                var profile = BackendService.ResolveProfile(await api.GetQualityProfilesAsync(instance.Settings), instance.Settings.QualityProfile)
                              ?? throw new BackendException($"quality profile '{instance.Settings.QualityProfile}' not found on {instance.Name}");
                var root = instance.Settings.RootFolder ?? (await api.GetRootFoldersAsync(instance.Settings)).FirstOrDefault()
                           ?? throw new BackendException($"no root folder on {instance.Name}");

                request.ExternalItemId = request.ContentType == ContentType.Movie
                    ? await _radarr.AddMovieAsync(instance.Settings, item, profile.Id, root)
                    : await _sonarr.AddSeriesAsync(instance.Settings, item, profile.Id, root, instance.Settings.SeriesType, request.Seasons, request.MonitorFutureSeasons);

                request.QualityProfileId = profile.Id;
                request.RootFolderPath = root;
                request.Status = RequestStatus.Approved;
                request.Decided = Clock();
                _requests.Update(request);

                var typeDetail = request.ContentType == ContentType.Tv ? $", type {instance.Settings.SeriesType ?? "standard"}" : string.Empty;
                _audit.Record(actor, "request.send", "request", request.Id.ToString(), $"{instance.Name} item {request.ExternalItemId}, profile {profile.Name}, root {root}{typeDetail}");

                return true;
            }
            catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException)
            {
                _logger.Error(ex, "Sending request {0} to {1} failed", request.Id, instance.Name);

                request.Status = RequestStatus.Failed;
                request.DecisionReason = ex.Message;
                request.Decided = Clock();
                _requests.Update(request);

                _audit.Record(actor, "request.failed", "request", request.Id.ToString(), ex.Message);

                return false;
            }
        }

        private async Task PostApprovalCard(Request request, BackendInstance instance, MediaLookupResult item, string ownerName)
        {
            var adminChannel = ulong.Parse(_guildSetup.GetConfig().AdminChannelId);

            await _gateway.SendMessageAsync(adminChannel, RequestCards.Approval(request, ownerName, instance.Name, item));
        }

        private async Task Notify(Request request, string message)
        {
            var owner = _users.Find(request.OwnerUserId);
            var target = owner?.Status == UserStatus.Active && ulong.TryParse(owner.PrivateChannelId, out var channel)
                ? channel
                : ulong.Parse(_guildSetup.GetConfig().AdminChannelId);

            try
            {
                await _gateway.SendMessageAsync(target, new InteractionReply { Content = message });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not notify about request {0}", request.Id);
            }
        }

        private Task<MediaLookupResult> Lookup(BackendInstance instance, ContentType type, int externalId)
        {
            return type == ContentType.Movie
                ? _radarr.LookupTmdbAsync(instance.Settings, externalId)
                : _sonarr.LookupTvdbAsync(instance.Settings, externalId);
        }

        private (BackendInstance Instance, string Error) Resolve(RequestCaller caller, ContentType type)
        {
            if (type == ContentType.Music)
            {
                return (null, DiscordText.MusicNotYet);
            }

            var instance = caller.AsAdmin ? _backends.Default(type) : _backends.ForUser(AccessFor(caller.User, type), type);

            return instance == null ? (null, DiscordText.NoBackend(type)) : (instance, null);
        }

        private UserMediaAccess AccessFor(RequestarrUser user, ContentType type)
        {
            return _access.GetForUser(user.Id).FirstOrDefault(a => a.ContentType == type) ??
                   new UserMediaAccess { UserId = user.Id, ContentType = type, State = UserManagementService.DefaultAccess[type] };
        }
    }

    public static class MediaLookupExtensions
    {
        public static int? ExternalId(this MediaLookupResult result, ContentType type) => type == ContentType.Tv ? result.TvdbId : result.TmdbId;
    }
}
