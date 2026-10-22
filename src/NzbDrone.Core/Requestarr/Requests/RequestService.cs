using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Metadata;
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

        // Anime with sub/dub on: the instance's settings, and the language the series is set to.
        public AnimeConfig Anime { get; init; }
        public AnimeLanguage? Language { get; init; }
    }

    public record LookupOutcome<T>(T Value, string Error)
    {
        public static LookupOutcome<T> Ok(T value) => new(value, null);

        public static LookupOutcome<T> Fail(string error) => new(default, error);
    }

    // All: every regular season now plus new ones as they air (Future), specials excluded.
    // Otherwise exactly Seasons (0 = specials), plus new seasons when Future is set.
    public record SeasonChoice(bool All, IReadOnlyCollection<int> Seasons, bool Future)
    {
        public static SeasonChoice Everything => new(true, Array.Empty<int>(), true);
    }

    public enum RequestOutcome
    {
        Sent,
        Pending,
        Following,
        AlreadyAvailable,
        Refused,
        Failed
    }

    // A request outcome with the request it created or joined (for Cancel and the final status line).
    // Where a series already in Sonarr really stands (anime replies say this, never "available" early).
    public enum LibraryStanding
    {
        Searching,
        Downloading,
        PartlyAvailable,
        Available
    }

    public record RequestResult(bool Success, string Message, Request Request, RequestOutcome Outcome) : ServiceResult(Success, Message)
    {
        // Replaces the outcome's header on the final card (a series' real state in the library).
        public string Header { get; init; }

        public static RequestResult Refused(string message) => new(false, message, null, RequestOutcome.Refused);

        // The title as looked up, for the card on the final status (null when the lookup failed).
        public MediaLookupResult Item { get; init; }
    }

    public interface IRequestService
    {
        Task<LookupOutcome<List<MediaLookupResult>>> SearchAsync(RequestCaller caller, ContentType type, string query);
        Task<LookupOutcome<TitleState>> DetailAsync(RequestCaller caller, ContentType type, int externalId);
        Task<RequestResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null, AnimeLanguage? language = null);
        Task<ServiceResult> ApproveAsync(int requestId, Actor admin, string adminName);
        Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName);
        Task<ServiceResult> CancelAsync(RequestCaller caller, int requestId);
        Task<ServiceResult> AdminCancelAsync(int requestId, string reason, Actor admin, string adminName);

        // Closes a pending request as already available (the title is already there).
        Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin, string adminName);

        // What Sonarr / Radarr now say about a sent request. A change (downloading, stuck, failed,
        // seasons available, all there) reposts its messages and feed card; no change does nothing.
        Task<bool> ApplyProgressAsync(Request request, ProgressSnapshot snapshot);

        // More seasons for a TV request, by its owner, a follower or an admin: no approval, no quota.
        Task<ServiceResult> AddSeasonsAsync(int requestId, RequestarrUser user, bool asAdmin, Actor actor, SeasonChoice add);

        // Whether the request is one the caller can add seasons to.
        bool CanAddSeasons(Request request, RequestarrUser user, bool asAdmin);

        // A request in trouble: ask its instance to search again (download failed or stuck), or try
        // adding it again (it couldn't be added).
        Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin);
        Task<ServiceResult> RetrySendAsync(int requestId, Actor admin);

        // Everyone on a request (the requester and followers): their access for its type, their limit
        // used, and how many of their requests wait for approval. One line each.
        IReadOnlyList<string> PeopleOn(Request request);

        // Remembers the message a request was made in, so it can be edited after a decision.
        void AttachMessage(int requestId, ulong channelId, ulong messageId);

        // Remembers a follower's own message about a request, reposted when the request changes.
        void AttachFollowerMessage(int requestId, int userId, ulong channelId, ulong messageId);

        // The request's real state as a request message: status line, card, and a Cancel button
        // only while it can still be cancelled.
        Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item = null);

        // The same for someone viewing their own message: a follower's says whose request it is.
        Task<InteractionReply> StatusReplyForAsync(Request request, RequestarrUser viewer, string note = null);

        // The title as the request's instance has it now (null when it can't be looked up).
        Task<MediaLookupResult> ItemForAsync(Request request);

        // A title as an instance has it now, by its TMDB (movie) or TVDB (TV) ID; null when it can't be looked up.
        Task<MediaLookupResult> LookupItemAsync(BackendInstance instance, ContentType type, int externalId);

        // After a removal: the whole title closes the request as removed; seasons leave the request
        // (and close it once none are left). Its people get their messages reposted with the note.
        Task ApplyRemovalAsync(Request request, IReadOnlyCollection<int> seasons, string note);

        // The owner's message edited in place (a removal opened or closed: its Remove button follows).
        Task RefreshOwnerMessageAsync(Request request);
        List<Request> List(RequestarrUser user, bool all, RequestStatus? status, ContentType? type);
        string OwnerName(Request request);
        string DisplayName(int? userId);
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
        private readonly IMetadataService _metadata;
        private readonly IAnimeService _anime;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestFeed _feed;
        private readonly Removals.IRemovalProposalRepository _removals;
        private readonly NzbDrone.Core.Messaging.Events.IEventAggregator _events;
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
                              IMetadataService metadata,
                              IAnimeService anime,
                              IUserIdentityRepository identities,
                              IRequestFeed feed,
                              Removals.IRemovalProposalRepository removals,
                              NzbDrone.Core.Messaging.Events.IEventAggregator events,
                              Logger logger)
        {
            _removals = removals;
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
            _metadata = metadata;
            _anime = anime;
            _identities = identities;
            _feed = feed;
            _events = events;
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

                var top = results.Where(r => r.ExternalId(type).HasValue).Take(MaxSearchResults).ToList();

                if (!top.Any())
                {
                    return LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.NothingFound);
                }

                await _metadata.EnrichAsync(type, top);

                return LookupOutcome<List<MediaLookupResult>>.Ok(top);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Lookup on {0} failed", instance.Name);
                return LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.RequestsPaused(type, instance.Name));
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
                return LookupOutcome<TitleState>.Fail(DiscordText.RequestsPaused(type, instance.Name));
            }

            if (item == null)
            {
                return LookupOutcome<TitleState>.Fail(DiscordText.TitleNotFound);
            }

            await _metadata.EnrichAsync(type, new[] { item });

            var forTitle = _requests.ForTitle(type, externalId).Where(r => r.BackendId == instance.Id).Newest().ToList();
            var active = forTitle.FirstOrDefault(r => r.IsActive);
            var anime = type == ContentType.Tv && item.IsAnime ? _anime.ConfigFor(instance) : null;
            AnimeLanguage? language = null;

            if (anime is { Enabled: true })
            {
                language = active?.Language ?? (item.LibraryId.HasValue ? _anime.LanguageOf(item.TagIds, await _anime.TagsAsync(instance)) : null);
            }

            return LookupOutcome<TitleState>.Ok(new TitleState
            {
                Item = item,
                Instance = instance,
                Active = active,
                ActiveOwner = active == null ? null : OwnerName(active),
                CallerOwnsActive = active != null && caller.User != null && active.OwnerUserId == caller.User.Id,
                CallerFollows = active != null && caller.User != null && _subscribers.ForRequest(active.Id).Any(s => s.UserId == caller.User.Id),
                LastDenied = active == null ? forTitle.FirstOrDefault(r => r.Status == RequestStatus.Denied) : null,
                Anime = anime is { Enabled: true } ? anime : null,
                Language = language
            });
        }

        public async Task<RequestResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null, AnimeLanguage? language = null)
        {
            var seen = new System.Runtime.CompilerServices.StrongBox<MediaLookupResult>();
            var result = await RequestCoreAsync(caller, type, externalId, seasons, language, seen);

            return result with { Item = seen.Value };
        }

        private async Task<RequestResult> RequestCoreAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons, AnimeLanguage? language, System.Runtime.CompilerServices.StrongBox<MediaLookupResult> seen)
        {
            if (type == ContentType.Music)
            {
                return RequestResult.Refused(DiscordText.MusicNotYet);
            }

            var access = caller.AsAdmin ? null : AccessFor(caller.User, type);

            if (!caller.AsAdmin && access.State == AccessState.Off)
            {
                _audit.Record(caller.Actor, "request.refused", "title", $"{type}:{externalId}", "access off");
                return RequestResult.Refused(DiscordText.AccessOff(type));
            }

            var detail = await DetailAsync(caller, type, externalId);

            if (detail.Error != null)
            {
                return RequestResult.Refused(detail.Error);
            }

            var state = detail.Value;
            var item = state.Item;
            seen.Value = item;

            // Anime: one language per series per instance, settled by the instance's priority rule.
            var anime = type == ContentType.Tv && item.IsAnime ? _anime.ConfigFor(state.Instance) : null;

            if (anime is { Enabled: true })
            {
                var tags = await _anime.TagsAsync(state.Instance);

                // In the library, Sonarr's tag is the truth (like any title there, it's good news);
                // otherwise the open request's language.
                var existing = item.LibraryId.HasValue ? _anime.LanguageOf(item.TagIds, tags) : state.Active?.Language;
                language ??= AnimeDefaults.Default(anime.Priority, existing);

                if (existing.HasValue && existing != language)
                {
                    return AnimeDefaults.Winner(anime.Priority, existing.Value, language.Value) == existing.Value
                        ? await StaysAs(caller, state, existing.Value, anime.Priority)
                        : await SwitchTo(caller, state, language.Value);
                }
            }
            else
            {
                language = null;
            }

            if (RequestCards.InLibraryAndDone(state))
            {
                return new RequestResult(true, DiscordText.AlreadyInLibrary, null, RequestOutcome.AlreadyAvailable);
            }

            // Duplicate: attach as subscriber instead of a second request. No quota cost.
            if (state.Active != null)
            {
                if (state.CallerOwnsActive)
                {
                    return new RequestResult(false, DiscordText.AlreadyRequestedByYou, state.Active, RequestOutcome.Refused);
                }

                if (state.CallerFollows)
                {
                    return new RequestResult(false, DiscordText.AlreadyFollowing, state.Active, RequestOutcome.Following);
                }

                await Subscribe(state.Active, caller.User.Id, state.Item);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString());

                return new RequestResult(true, DiscordText.Subscribed(state.Active.Label, state.ActiveOwner, state.Active.Id), state.Active, RequestOutcome.Following);
            }

            var chosen = new List<int>();
            var future = false;

            if (type == ContentType.Tv)
            {
                seasons ??= new SeasonChoice(true, Array.Empty<int>(), item.Continuing);
                chosen = (seasons.All ? item.Seasons : seasons.Seasons.Where(s => item.Seasons.Contains(s) || (s == 0 && item.HasSpecials))).Distinct().OrderBy(s => s).ToList();
                future = seasons.Future;

                if (!chosen.Any() && !future)
                {
                    return RequestResult.Refused(DiscordText.ChooseSeasons);
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
                    return RequestResult.Refused(DiscordText.QuotaReached(type, access.QuotaLimit.Value, (int)window.TotalDays, nextSlot));
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
                Language = language,
                PosterUrl = item.PosterUrl,
                Requested = Clock()
            });

            var seasonDetail = type == ContentType.Tv ? $", seasons {DiscordText.Seasons(request)}{(language.HasValue ? $", {DiscordText.Label(language.Value)}" : string.Empty)}" : string.Empty;
            _audit.Record(caller.Actor, "request.create", "request", request.Id.ToString(), $"{type} {request.Label} on {state.Instance.Name}{seasonDetail}");

            if (caller.AsAdmin || access.State == AccessState.AutoApprove)
            {
                request.DecidedByUserId = caller.AsAdmin ? caller.User.Id : null;
                var (sent, _) = await SendAsync(request, state.Instance, item, caller.AsAdmin ? caller.Actor : Actor.System);

                await _feed.PostAsync(request, item);

                if (request.Status == RequestStatus.Available)
                {
                    return new RequestResult(true, DiscordText.RequestAlreadyAvailableStatus(request.Label, request.Id), request, RequestOutcome.AlreadyAvailable);
                }

                return sent
                    ? new RequestResult(true, DiscordText.RequestSent(request.Label, request.Id, state.Instance.Name), request, RequestOutcome.Sent)
                    : new RequestResult(false, DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason), request, RequestOutcome.Failed);
            }

            await PostApprovalCard(request, state.Instance, item, caller.Name);
            await _feed.PostAsync(request, item);
            _events.PublishEvent(new AdminQueueChangedEvent());

            return new RequestResult(true, DiscordText.RequestPending(request.Label, request.Id), request, RequestOutcome.Pending);
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

            var current = await TryLookup(instance, request);

            if (current?.LibraryId != null)
            {
                return await CloseAsAvailable(request, admin, current);
            }

            request.DecidedByUserId = admin.UserId;
            _audit.Record(admin, "request.approve", "request", request.Id.ToString());

            var (sent, item) = await SendAsync(request, instance, current, admin);

            if (request.Status == RequestStatus.Available)
            {
                await RefreshOrNotify(request, item, DiscordText.RequestAlreadyAvailableNotice(request.Label, request.Id));
                return ServiceResult.Ok(DiscordText.AdminMarkedAvailable(request));
            }

            await RefreshOrNotify(request, item, sent
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

            var inLibrary = await TryLookup(_backends.Find(request.BackendId ?? 0), request);

            if (inLibrary?.LibraryId != null)
            {
                return await CloseAsAvailable(request, admin, inLibrary);
            }

            request.Status = RequestStatus.Denied;
            request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            request.Decided = Clock();
            request.DecidedByUserId = admin.UserId;
            _requests.Update(request);

            _audit.Record(admin, "request.deny", "request", request.Id.ToString(), request.DecisionReason);
            await RefreshOrNotify(request, null, DiscordText.RequestDeniedNotice(request.Label, request.Id, request.DecisionReason));

            return ServiceResult.Ok(DiscordText.AdminDenied(request));
        }

        public async Task<ServiceResult> AdminCancelAsync(int requestId, string reason, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (!AdminCanCancel(request))
            {
                return ServiceResult.Fail(request.Status == RequestStatus.Approved ? DiscordText.CannotCancelDownloading(request.Id) : DiscordText.CannotCancel(request.Status));
            }

            var instance = _backends.Find(request.BackendId ?? 0);
            var wasSent = request.Status == RequestStatus.Approved && request.ExternalItemId.HasValue;

            if (wasSent)
            {
                // Already in Sonarr / Radarr: stop the search there, delete nothing.
                try
                {
                    await _backends.ApiFor(instance).SetMonitoredAsync(instance.Settings, request.ExternalItemId.Value, false);
                }
                catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException || ex is NullReferenceException)
                {
                    _logger.Warn(ex, "Request {0}: could not unmonitor it on {1}", request.Id, instance?.Name);
                    return ServiceResult.Fail(DiscordText.CancelNotStopped(request, instance?.Name));
                }
            }
            else
            {
                var available = await TryLookup(instance, request);

                if (available?.LibraryId != null)
                {
                    return await CloseAsAvailable(request, admin, available);
                }
            }

            request.Status = RequestStatus.Cancelled;
            request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            request.Decided = Clock();
            request.DecidedByUserId = admin.UserId;
            _requests.Update(request);

            _audit.Record(admin, "request.cancel", "request", request.Id.ToString(), wasSent ? $"unmonitored on {instance.Name}{(request.DecisionReason != null ? "; " + request.DecisionReason : string.Empty)}" : request.DecisionReason);
            await RefreshOrNotify(request, null, DiscordText.RequestCancelledByAdminNotice(request.Label, request.Id, request.DecisionReason));

            return ServiceResult.Ok(wasSent ? DiscordText.AdminCancelledSent(request, instance.Name) : DiscordText.AdminCancelled(request));
        }

        public async Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending && !Admin.TroubleSource.InTrouble(request))
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            return await CloseAsAvailable(request, admin, null);
        }

        // The title is already there: the request ends as available (never "denied"), the user's
        // message and the approval card say so, and the user is told.
        private async Task<ServiceResult> CloseAsAvailable(Request request, Actor admin, MediaLookupResult item)
        {
            request.Status = RequestStatus.Available;
            request.DecisionReason = null;
            request.Decided = Clock();
            request.Fulfilled = Clock();
            request.DecidedByUserId = admin.UserId;
            _requests.Update(request);

            _audit.Record(admin, "request.available", "request", request.Id.ToString(), item?.LibraryId != null ? "already in the library" : "marked by the admin");
            await RefreshOrNotify(request, item, DiscordText.RequestAlreadyAvailableNotice(request.Label, request.Id));

            return ServiceResult.Ok(DiscordText.AdminMarkedAvailable(request));
        }

        // The admin can cancel while it waits for approval, or once sent while nothing is
        // downloading or there yet (later, removal is the way: task 4).
        public static bool AdminCanCancel(Request request) =>
            request.Status == RequestStatus.Pending ||
            Admin.TroubleSource.InTrouble(request) ||
            (request.Status == RequestStatus.Approved && request.Progress == DownloadProgress.None && !(request.AvailableSeasons?.Any() ?? false));

        public async Task<bool> ApplyProgressAsync(Request request, ProgressSnapshot snapshot)
        {
            if (snapshot == null || !request.IsTracked)
            {
                return false;
            }

            if (snapshot.SizeLeft != request.ProgressSizeLeft)
            {
                request.ProgressSizeLeft = snapshot.SizeLeft;
                request.ProgressSince = Clock();
            }

            var seasons = snapshot.AvailableSeasons.Distinct().OrderBy(s => s).ToList();
            var detail = snapshot.Progress is DownloadProgress.Stalled or DownloadProgress.Failed ? snapshot.Detail : null;
            var changed = snapshot.Complete || snapshot.Progress != request.Progress || !seasons.SequenceEqual(request.AvailableSeasons ?? new List<int>()) || detail != request.ProgressDetail;

            if (!changed)
            {
                _requests.Update(request);
                return false;
            }

            var instance = _backends.Find(request.BackendId ?? 0);
            request.AvailableSeasons = seasons;
            request.ProgressDetail = detail;

            if (snapshot.Complete)
            {
                request.Status = RequestStatus.Available;
                request.Progress = DownloadProgress.Imported;
                request.Fulfilled = Clock();
                request.ProgressSizeLeft = null;
                _requests.Update(request);
                _audit.Record(Actor.System, "request.fulfilled", "request", request.Id.ToString(), request.ContentType == ContentType.Tv ? $"seasons {DiscordText.PlainSeasons(seasons)}" : null);
                await RefreshOrNotify(request, null, DiscordText.RequestFulfilledNotice(request.Label, request.Id));
                return true;
            }

            request.Progress = snapshot.Progress;
            _requests.Update(request);
            _audit.Record(Actor.System, "request.progress", "request", request.Id.ToString(), $"{snapshot.Progress}{(seasons.Any() ? $", {DiscordText.PlainSeasons(seasons)} available" : string.Empty)}{(detail != null ? ": " + detail : string.Empty)}");
            await RefreshOrNotify(request, null, DiscordText.RequestProgressNotice(request, instance?.Name));

            return true;
        }

        public bool CanAddSeasons(Request request, RequestarrUser user, bool asAdmin) =>
            request is { ContentType: ContentType.Tv, IsActive: true } &&
            (asAdmin || (user != null && (request.OwnerUserId == user.Id || _subscribers.ForRequest(request.Id).Any(s => s.UserId == user.Id))));

        public async Task<ServiceResult> AddSeasonsAsync(int requestId, RequestarrUser user, bool asAdmin, Actor actor, SeasonChoice add)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (!CanAddSeasons(request, user, asAdmin))
            {
                return ServiceResult.Fail(request.ContentType != ContentType.Tv || request.IsActive ? DiscordText.NotYourRequest : DiscordText.CannotAddSeasons(request.Status));
            }

            var seasons = (add?.Seasons ?? Array.Empty<int>()).Except(request.Seasons).Distinct().OrderBy(s => s).ToList();
            var future = (add?.Future ?? false) && !request.MonitorFutureSeasons;

            if (!seasons.Any() && !future)
            {
                return ServiceResult.Fail(DiscordText.NothingNewToAdd);
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            // Sent already: Sonarr monitors and searches the new seasons now. Pending: they go with it on approval.
            if (request.Status != RequestStatus.Pending && request.ExternalItemId.HasValue)
            {
                if (instance == null || !instance.Enable)
                {
                    return ServiceResult.Fail(DiscordText.NoBackend(ContentType.Tv));
                }

                try
                {
                    await _sonarr.AddSeasonsAsync(instance.Settings, request.ExternalItemId.Value, seasons, future);
                }
                catch (BackendException ex)
                {
                    _logger.Warn(ex, "Adding seasons to request {0} on {1} failed", request.Id, instance.Name);
                    return ServiceResult.Fail(DiscordText.BackendUnavailable(instance.Name));
                }
            }

            request.Seasons = request.Seasons.Concat(seasons).Distinct().OrderBy(s => s).ToList();
            request.MonitorFutureSeasons |= future;

            if (request.Status == RequestStatus.Available)
            {
                // Fulfilled before: the new seasons are on their way, so it's tracked again.
                request.Status = RequestStatus.Approved;
                request.Progress = DownloadProgress.None;
            }

            _requests.Update(request);

            var added = DiscordText.AddedSeasons(seasons, future);
            _audit.Record(actor, "request.seasons.add", "request", request.Id.ToString(), added);
            await RefreshOrNotify(request, null, DiscordText.SeasonsAddedNotice(request.Label, request.Id, added), skipUserId: user?.Id);

            return ServiceResult.Ok(DiscordText.SeasonsAdded(request, added, request.Status == RequestStatus.Pending));
        }

        public async Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Approved || !request.ExternalItemId.HasValue)
            {
                return ServiceResult.Fail(DiscordText.NothingToSearch(request));
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            try
            {
                await _backends.ApiFor(instance).SearchAsync(instance.Settings, request.ExternalItemId.Value);
            }
            catch (Exception ex) when (ex is BackendException || ex is NullReferenceException)
            {
                _logger.Warn(ex, "Request {0}: search on {1} failed", request.Id, instance?.Name);
                return ServiceResult.Fail(DiscordText.RequestsPaused(request.ContentType, instance?.Name ?? DiscordText.Label(request.ContentType)));
            }

            _audit.Record(admin, "request.search", "request", request.Id.ToString(), instance.Name);
            return ServiceResult.Ok(DiscordText.SearchingAgain(request, instance.Name));
        }

        public async Task<ServiceResult> RetrySendAsync(int requestId, Actor admin)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Failed)
            {
                return ServiceResult.Fail(DiscordText.NothingToRetry(request));
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            if (instance == null || !instance.Enable)
            {
                return ServiceResult.Fail(DiscordText.NoBackend(request.ContentType));
            }

            _audit.Record(admin, "request.retry", "request", request.Id.ToString());
            request.DecisionReason = null;
            var (sent, item) = await SendAsync(request, instance, null, admin);

            await RefreshOrNotify(request, item, sent
                ? DiscordText.RequestApprovedNotice(request.Label, request.Id, instance.Name)
                : DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));

            return sent || request.Status == RequestStatus.Available
                ? ServiceResult.Ok(DiscordText.RequestSent(request.Label, request.Id, instance.Name))
                : ServiceResult.Fail(DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));
        }

        public IReadOnlyList<string> PeopleOn(Request request)
        {
            var people = new[] { (UserId: request.OwnerUserId, Role: DiscordText.Requester) }
                .Concat(_subscribers.ForRequest(request.Id).Select(s => (s.UserId, Role: DiscordText.Follower)));
            var lines = new List<string>();

            foreach (var (userId, role) in people)
            {
                var user = _users.Find(userId);

                if (user == null)
                {
                    continue;
                }

                var access = AccessFor(user, request.ContentType);
                var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
                var used = _requests.OwnedBy(user.Id).Count(r => r.ContentType == request.ContentType && r.IsActive && r.Requested > Clock() - window);
                var pending = _requests.OwnedBy(user.Id).Count(r => r.Status == RequestStatus.Pending);

                lines.Add(DiscordText.PersonLine(user.DisplayName, role, request.ContentType, access.State, used, access.QuotaLimit, (int)window.TotalDays, pending));
            }

            return lines;
        }

        // A follower joins: no quota cost; the admin feed shows who follows.
        private async Task Subscribe(Request request, int userId, MediaLookupResult item, bool postFeed = true)
        {
            _subscribers.Insert(new RequestSubscriber { RequestId = request.Id, UserId = userId, Added = Clock() });

            if (postFeed)
            {
                await _feed.PostAsync(request, item);
            }
        }

        public void AttachFollowerMessage(int requestId, int userId, ulong channelId, ulong messageId)
        {
            var subscriber = _subscribers.ForRequest(requestId).FirstOrDefault(s => s.UserId == userId);

            if (subscriber == null)
            {
                return;
            }

            subscriber.MessageChannelId = channelId.ToString();
            subscriber.MessageId = messageId.ToString();
            _subscribers.Update(subscriber);
        }

        // The requested language loses to the series' current one: follow it (or, in the library,
        // the good news that it is there).
        private async Task<RequestResult> StaysAs(RequestCaller caller, TitleState state, AnimeLanguage existing, AnimePriority priority)
        {
            var label = Titles.WithYear(state.Item.Title, state.Item.Year);

            if (state.Item.LibraryId.HasValue)
            {
                var standing = await StandingAsync(state);
                var result = await InLibraryResult(caller, state, standing, $"stays {DiscordText.Label(existing)}");

                return result with { Message = DiscordText.AnimeStaysInLibrary(label, existing, priority, standing) };
            }

            if (state.Active == null)
            {
                return new RequestResult(true, DiscordText.AnimeStaysInLibrary(label, existing, priority, LibraryStanding.Available), null, RequestOutcome.AlreadyAvailable);
            }

            if (state.CallerOwnsActive)
            {
                return new RequestResult(false, DiscordText.AnimeStaysYours(label, existing, priority), state.Active, RequestOutcome.Refused);
            }

            if (!state.CallerFollows)
            {
                await Subscribe(state.Active, caller.User.Id, state.Item);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), $"asked for {DiscordText.Label(existing == AnimeLanguage.Dub ? AnimeLanguage.Sub : AnimeLanguage.Dub)}, stays {DiscordText.Label(existing)}");
            }

            return new RequestResult(true, DiscordText.AnimeStays(label, existing, priority, state.ActiveOwner, state.Active.Id), state.Active, RequestOutcome.Following);
        }

        // The requested language wins: the series switches. In the library its Sonarr tag changes;
        // a pending request changes language. Everyone on the request is told; nothing is deleted.
        private async Task<RequestResult> SwitchTo(RequestCaller caller, TitleState state, AnimeLanguage language)
        {
            var item = state.Item;
            var label = Titles.WithYear(item.Title, item.Year);

            try
            {
                if (item.LibraryId.HasValue)
                {
                    await _anime.SetLanguageAsync(state.Instance, item.LibraryId.Value, item.TagIds, language);
                }
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Switching {0} to {1} on {2} failed", label, language, state.Instance.Name);
                return RequestResult.Refused(DiscordText.BackendUnavailable(state.Instance.Name));
            }

            _audit.Record(caller.Actor, "anime.switch", "title", $"tv:{item.TvdbId}", $"{label} on {state.Instance.Name} now {DiscordText.Label(language)}");

            // Someone else's open request: the switcher follows it (joined before the reposts below,
            // so the feed card is posted once, already showing them).
            if (!item.LibraryId.HasValue && state.Active != null && !state.CallerOwnsActive && !state.CallerFollows)
            {
                await Subscribe(state.Active, caller.User.Id, item, postFeed: false);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), $"switched it to {DiscordText.Label(language)}");
            }

            // Every request for this title on the instance: its language changes and its people are told.
            foreach (var request in _requests.ForTitle(ContentType.Tv, item.TvdbId.Value).Where(r => r.BackendId == state.Instance.Id && r.IsActive))
            {
                request.Language = language;
                _requests.Update(request);
                await RefreshOrNotify(request, item, DiscordText.AnimeSwitchedNotice(request.Label, language), DiscordText.AnimeSwitchedNote(language), skipUserId: caller.User?.Id);
            }

            if (item.LibraryId.HasValue)
            {
                var standing = await StandingAsync(state);
                var result = await InLibraryResult(caller, state, standing, $"switched it to {DiscordText.Label(language)}");

                return result with { Message = DiscordText.AnimeSwitchedInLibrary(label, language, standing) };
            }

            if (state.Active == null)
            {
                return new RequestResult(true, DiscordText.AnimeSwitchedInLibrary(label, language, LibraryStanding.Available), null, RequestOutcome.AlreadyAvailable);
            }

            // Re-read: the loop above changed its language.
            var active = _requests.Find(state.Active.Id);

            if (state.CallerOwnsActive)
            {
                return new RequestResult(true, DiscordText.AnimeSwitchedYours(label, language), active, RequestOutcome.Pending);
            }

            return new RequestResult(true, DiscordText.AnimeSwitchedFollow(label, language, state.ActiveOwner, active.Id), active, RequestOutcome.Following);
        }

        // A series in Sonarr: its open request's state when there is one, otherwise the episodes on disk.
        private async Task<LibraryStanding> StandingAsync(TitleState state)
        {
            var active = state.Active == null ? null : _requests.Find(state.Active.Id);

            if (active?.Status == RequestStatus.Available)
            {
                return LibraryStanding.Available;
            }

            if (active?.Status == RequestStatus.Approved)
            {
                return active.Progress is DownloadProgress.Downloading or DownloadProgress.Stalled ? LibraryStanding.Downloading
                    : active.AvailableSeasons?.Any() == true ? LibraryStanding.PartlyAvailable
                    : LibraryStanding.Searching;
            }

            try
            {
                var episodes = await _sonarr.GetEpisodesAsync(state.Instance.Settings, state.Item.LibraryId.Value);
                var aired = episodes?.Where(e => e.SeasonNumber > 0 && e.AirDateUtc <= Clock()).ToList();

                if (aired == null || aired.Count == 0)
                {
                    return aired == null ? LibraryStanding.Available : LibraryStanding.Searching;
                }

                var withFiles = aired.Count(e => e.HasFile);
                return withFiles == 0 ? LibraryStanding.Searching : withFiles < aired.Count ? LibraryStanding.PartlyAvailable : LibraryStanding.Available;
            }
            catch (BackendException ex)
            {
                _logger.Debug(ex, "Episodes of {0} could not be read", state.Item.Title);
                return LibraryStanding.Available;
            }
        }

        // The reply for a series already in Sonarr: "available" only when it is; while it is still on
        // its way, the switcher follows the open request (or it's theirs).
        private async Task<RequestResult> InLibraryResult(RequestCaller caller, TitleState state, LibraryStanding standing, string why)
        {
            var header = DiscordText.StandingHeader(standing);

            if (standing == LibraryStanding.Available || state.Active == null)
            {
                return new RequestResult(true, null, null, standing == LibraryStanding.Available ? RequestOutcome.AlreadyAvailable : RequestOutcome.Sent) { Header = header };
            }

            if (!state.CallerOwnsActive && !state.CallerFollows && caller.User != null)
            {
                await Subscribe(state.Active, caller.User.Id, state.Item);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), why);
            }

            return new RequestResult(true, null, _requests.Find(state.Active.Id), state.CallerOwnsActive ? RequestOutcome.Sent : RequestOutcome.Following) { Header = header };
        }

        public void AttachMessage(int requestId, ulong channelId, ulong messageId)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return;
            }

            request.MessageChannelId = channelId.ToString();
            request.MessageId = messageId.ToString();
            _requests.Update(request);
        }

        public Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item = null) => StatusReplyAsync(request, item, null, null, null);

        public Task<InteractionReply> StatusReplyForAsync(Request request, RequestarrUser viewer, string note = null) =>
            StatusReplyAsync(request, null, null, note, viewer != null && viewer.Id != request.OwnerUserId ? OwnerName(request) : null);

        public Task<MediaLookupResult> ItemForAsync(Request request) => TryLookup(_backends.Find(request.BackendId ?? 0), request);

        public async Task<MediaLookupResult> LookupItemAsync(BackendInstance instance, ContentType type, int externalId)
        {
            try
            {
                var item = await Lookup(instance, type, externalId);

                if (item != null)
                {
                    await _metadata.EnrichAsync(type, new[] { item });
                }

                return item;
            }
            catch (BackendException)
            {
                return null;
            }
        }

        public async Task ApplyRemovalAsync(Request request, IReadOnlyCollection<int> seasons, string note)
        {
            if (seasons == null || seasons.Count == 0)
            {
                request.Status = RequestStatus.Removed;
            }
            else
            {
                request.Seasons = request.Seasons.Except(seasons).ToList();
                request.AvailableSeasons = (request.AvailableSeasons ?? new List<int>()).Except(seasons).ToList();

                if (request.Seasons.Count == 0 && !request.MonitorFutureSeasons)
                {
                    request.Status = RequestStatus.Removed;
                }
            }

            _requests.Update(request);
            _audit.Record(Actor.System, "request.removed", "request", request.Id.ToString(), seasons?.Any() == true ? $"seasons {string.Join(", ", seasons)}" : "whole title");
            await RefreshMessagesAsync(request, null, ownerRepost: true, note);
        }

        public async Task RefreshOwnerMessageAsync(Request request)
        {
            if (!ulong.TryParse(request.MessageChannelId, out var channel) || !ulong.TryParse(request.MessageId, out var message))
            {
                return;
            }

            try
            {
                await _gateway.EditMessageAsync(channel, message, WithRemoval(RequestCards.Status(request, null, _backends.Find(request.BackendId ?? 0)?.Name), request));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Request {0}: its message could not be edited", request.Id);
            }
        }

        // The owner's request message: Remove… on a downloaded title with no removal open; a line
        // while one is (the vote itself is in their channel).
        private InteractionReply WithRemoval(InteractionReply reply, Request request)
        {
            var open = _removals.ForRequest(request.Id).FirstOrDefault(p => p.IsOpen);

            if (open != null)
            {
                return new InteractionReply
                {
                    Content = DiscordLimits.Truncate($"{reply.Content}\n{DiscordText.FeedRemovalLine(open, 0, OwnerName(request)).Split(" · ")[0]}", DiscordLimits.MessageLength),
                    Embeds = reply.Embeds,
                    KeepEmbeds = reply.KeepEmbeds,
                    Buttons = reply.Buttons
                };
            }

            if (RequestCards.Downloaded(request))
            {
                reply.Buttons.Add(new InteractionButton { CustomId = $"{Removals.RemovalRoutes.User}:{request.Id}", Label = DiscordText.RemoveRequestLabel, Style = ButtonStyle.Secondary });
            }

            return reply;
        }

        private async Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item, string mention, string note, string followerOf)
        {
            var instance = _backends.Find(request.BackendId ?? 0);
            item ??= await TryLookup(instance, request);
            var reply = RequestCards.Status(request, item, instance?.Name, mention, note, followerOf);

            return followerOf == null ? WithRemoval(reply, request) : reply;
        }

        public async Task<ServiceResult> CancelAsync(RequestCaller caller, int requestId)
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
            await RefreshMessagesAsync(request, null, ownerRepost: false, notice: DiscordText.RequestCancelled(request.Label, request.Id));

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

        public string DisplayName(int? userId) => userId.HasValue ? _users.Find(userId.Value)?.DisplayName : null;

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
        private async Task<(bool Sent, MediaLookupResult Item)> SendAsync(Request request, BackendInstance instance, MediaLookupResult item, Actor actor)
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

                // Anime always goes in as Sonarr's Anime type (absolute numbering, scene mapping);
                // with sub/dub on, tagged with its language so the matching release profile applies.
                var seriesType = item.IsAnime ? "anime" : instance.Settings.SeriesType;
                var tags = request.Language.HasValue && await _anime.TagsAsync(instance) is { } animeTags ? new[] { animeTags.For(request.Language.Value) } : null;

                request.ExternalItemId = request.ContentType == ContentType.Movie
                    ? await _radarr.AddMovieAsync(instance.Settings, item, profile.Id, root)
                    : await _sonarr.AddSeriesAsync(instance.Settings, item, profile.Id, root, seriesType, request.Seasons, request.MonitorFutureSeasons, tags);

                request.QualityProfileId = profile.Id;
                request.RootFolderPath = root;
                request.Status = RequestStatus.Approved;
                request.Decided = Clock();
                _requests.Update(request);

                var typeDetail = request.ContentType == ContentType.Tv ? $", type {seriesType ?? "standard"}{(tags != null ? $", tag {tags[0]}" : string.Empty)}" : string.Empty;
                _audit.Record(actor, "request.send", "request", request.Id.ToString(), $"{instance.Name} item {request.ExternalItemId}, profile {profile.Name}, root {root}{typeDetail}");

                return (true, item);
            }
            catch (BackendException ex) when (AlreadyAdded(ex))
            {
                // Someone added it to the instance since the request was made: it's there.
                _logger.Info("Request {0}: {1} already has it", request.Id, instance.Name);
                request.Status = RequestStatus.Available;
                request.DecisionReason = null;
                request.Decided = Clock();
                request.Fulfilled = Clock();
                _requests.Update(request);
                _audit.Record(actor, "request.available", "request", request.Id.ToString(), $"already on {instance.Name}");

                return (false, item);
            }
            catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException)
            {
                _logger.Error(ex, "Sending request {0} to {1} failed", request.Id, instance.Name);

                request.Status = RequestStatus.Failed;
                request.DecisionReason = ex.Message;
                request.Decided = Clock();
                _requests.Update(request);

                _audit.Record(actor, "request.failed", "request", request.Id.ToString(), ex.Message);

                return (false, item);
            }
        }

        // Radarr / Sonarr reject a second add with "This movie / series has already been added".
        private static bool AlreadyAdded(BackendException ex) => ex.Message.Contains("already been added", StringComparison.OrdinalIgnoreCase);

        private async Task PostApprovalCard(Request request, BackendInstance instance, MediaLookupResult item, string ownerName)
        {
            var adminChannel = ulong.Parse(_guildSetup.GetConfig().AdminChannelId);

            var id = await _gateway.SendMessageAsync(adminChannel, RequestCards.Approval(request, ownerName, instance.Name, item));
            request.AdminMessageId = id.ToString();
            _requests.Update(request);
        }

        // After a request changes while its people are no longer in the request flow, each of them
        // gets the news as the newest message in their channel: their old message about it is
        // deleted and a fresh one, mentioning them, is posted with only the buttons valid now. The
        // owner's own Cancel click edits in place instead (ownerRepost false). The admin card is
        // edited in place. Returns whether the owner's message was handled (else: notice fallback).
        private async Task<bool> RefreshMessagesAsync(Request request, MediaLookupResult item = null, bool ownerRepost = true, string note = null, int? skipUserId = null, string notice = null)
        {
            var instance = _backends.Find(request.BackendId ?? 0);
            item ??= await TryLookup(instance, request);
            var handled = false;

            if (request.OwnerUserId != skipUserId && ulong.TryParse(request.MessageChannelId, out var channel) && ulong.TryParse(request.MessageId, out var message))
            {
                try
                {
                    var reply = WithRemoval(RequestCards.Status(request, item, instance?.Name, ownerRepost ? Mention(request.OwnerUserId) : null, note, null), request);

                    if (ownerRepost)
                    {
                        request.MessageId = (await Repost(channel, message, reply)).ToString();
                        _requests.Update(request);
                        handled = true;
                    }
                    else
                    {
                        handled = await _gateway.EditMessageAsync(channel, message, reply);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Request {0}: its message could not be updated", request.Id);
                }
            }
            else if (request.OwnerUserId == skipUserId)
            {
                handled = true;
            }

            await RefreshFollowers(request, item, instance, note, skipUserId, notice);
            await _feed.PostAsync(request, item);
            _events.PublishEvent(new AdminQueueChangedEvent());

            if (ulong.TryParse(request.AdminMessageId, out var card) && ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel))
            {
                try
                {
                    var decidedBy = DisplayName(request.DecidedByUserId);
                    await _gateway.EditMessageAsync(adminChannel, card, new InteractionReply { Content = DiscordText.DecidedCard(request, OwnerName(request), decidedBy), KeepEmbeds = true });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Request {0}: its approval card could not be edited", request.Id);
                }
            }

            return handled;
        }

        private async Task RefreshFollowers(Request request, MediaLookupResult item, BackendInstance instance, string note, int? skipUserId, string notice)
        {
            var owner = OwnerName(request);

            foreach (var follower in _subscribers.ForRequest(request.Id).Where(f => f.UserId != skipUserId))
            {
                if (ulong.TryParse(follower.MessageChannelId, out var channel) && ulong.TryParse(follower.MessageId, out var message))
                {
                    try
                    {
                        var reply = RequestCards.Status(request, item, instance?.Name, Mention(follower.UserId), note, owner);
                        follower.MessageId = (await Repost(channel, message, reply)).ToString();
                        _subscribers.Update(follower);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Request {0}: follower {1}'s message could not be reposted", request.Id, follower.UserId);
                    }
                }

                if (notice != null)
                {
                    await NotifyUser(follower.UserId, notice);
                }
            }
        }

        // Deletes the old message and posts the new one at the bottom. If the old one can't be
        // deleted, the new one is posted anyway and the old one loses its buttons.
        private async Task<ulong> Repost(ulong channel, ulong old, InteractionReply reply)
        {
            var deleted = false;

            try
            {
                deleted = await _gateway.DeleteMessageAsync(channel, old);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not delete message {0}", old);
            }

            if (!deleted)
            {
                try
                {
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { Content = reply.Content, KeepEmbeds = true });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not strip the buttons from message {0}", old);
                }
            }

            return await _gateway.SendMessageAsync(channel, reply);
        }

        private string Mention(int userId)
        {
            var discordId = _identities.GetForUser(userId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;
            return discordId == null ? null : $"<@{discordId}>";
        }

        // The request's messages show the outcome; a separate notice only to whoever has no message
        // to show it (requests from before messages were stored, or a message that is gone).
        private async Task RefreshOrNotify(Request request, MediaLookupResult item, string notice, string note = null, int? skipUserId = null)
        {
            if (!await RefreshMessagesAsync(request, item, ownerRepost: true, note, skipUserId, notice))
            {
                await Notify(request, notice);
            }
        }

        private async Task NotifyUser(int userId, string message)
        {
            var user = _users.Find(userId);

            if (user?.Status != UserStatus.Active || !ulong.TryParse(user.PrivateChannelId, out var channel))
            {
                return;
            }

            try
            {
                await _gateway.SendMessageAsync(channel, new InteractionReply { Content = $"{Mention(userId)} {message}".Trim() });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not notify user {0}", userId);
            }
        }

        private async Task<MediaLookupResult> TryLookup(BackendInstance instance, Request request)
        {
            if (instance == null || !request.ExternalId.HasValue)
            {
                return null;
            }

            try
            {
                var item = await Lookup(instance, request.ContentType, request.ExternalId.Value);

                if (item != null)
                {
                    await _metadata.EnrichAsync(request.ContentType, new[] { item });
                }

                return item;
            }
            catch (BackendException)
            {
                return null;
            }
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

            // A type with no instance that has ever connected isn't shown to users, so it can't be requested.
            if (!caller.AsAdmin && !_backends.EverConnected(type))
            {
                return (null, DiscordText.TypeNotAvailable(type));
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
