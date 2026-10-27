using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Removals
{
    // Every minute: reminders, requester timeouts, closing votes, and prompts for users who gained access.
    public class RemovalCheckCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }

    // A title that can be removed: where it is, what is on disk, and its open request and requester
    // (null when it was never requested through Requestarr, or the requester was removed).
    public record RemovalTarget(BackendInstance Instance, LibraryTitle Title, Request Request, RequestarrUser Requester)
    {
        public ContentType Type => DiscordText.TypeOf(Instance);

        public string Label => Titles.WithYear(Title.Title, Title.Year);

        public int? ExternalId => Type == ContentType.Tv ? Title.TvdbId : Title.TmdbId;
    }

    public interface IRemovalService
    {
        RemovalProposal Find(int id);

        // The proposal the request's cards show: an open one, else the latest.
        RemovalProposal LatestFor(int requestId);

        IReadOnlyList<RemovalFlag> FlagsOn(int proposalId);

        Task<LookupOutcome<RemovalTarget>> TargetAsync(int backendId, int itemId);
        Task<LookupOutcome<RemovalTarget>> TargetForRequestAsync(int requestId);

        // The library of every connected Sonarr and Radarr, by title (titles nobody requested too).
        Task<List<RemovalTarget>> SearchLibraryAsync(string term);

        // User-initiated: the requester removes their own title (or seasons), straight to the vote.
        Task<ServiceResult> StartByUserAsync(RequestarrUser user, Actor actor, int requestId, IReadOnlyCollection<int> seasons);

        // Admin-initiated: an owned title asks the requester first, an ownerless one goes to the vote,
        // and force deletes now.
        Task<ServiceResult> StartByAdminAsync(Actor admin, string adminName, int? adminUserId, int backendId, int itemId, IReadOnlyCollection<int> seasons, bool force);

        Task<ServiceResult> AnswerAsync(int proposalId, RequestarrUser user, Actor actor, bool yes);
        Task<ServiceResult> SetKeepAsync(int proposalId, RequestarrUser user, Actor actor, bool keep);
        Task<ServiceResult> ForceAsync(int proposalId, Actor admin, string adminName, int? adminUserId);
        Task<ServiceResult> DropAsync(int proposalId, Actor admin, string adminName, int? adminUserId);

        Task TickAsync();
    }

    public class RemovalService : IRemovalService, IExecute<RemovalCheckCommand>
    {
        private readonly IRemovalProposalRepository _proposals;
        private readonly IRemovalFlagRepository _flags;
        private readonly IRemovalPromptRepository _prompts;
        private readonly IRequestRepository _requests;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IUserIdentityRepository _identities;
        private readonly IBackendService _backends;
        private readonly IRequestService _requestService;
        private readonly IRequestFeed _feed;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IAuditService _audit;
        private readonly IRemovalSettings _settings;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public RemovalService(IRemovalProposalRepository proposals,
                              IRemovalFlagRepository flags,
                              IRemovalPromptRepository prompts,
                              IRequestRepository requests,
                              IRequestarrUserRepository users,
                              IUserMediaAccessRepository access,
                              IUserIdentityRepository identities,
                              IBackendService backends,
                              IRequestService requestService,
                              IRequestFeed feed,
                              IDiscordGuildGateway gateway,
                              IGuildSetupService guildSetup,
                              IAuditService audit,
                              IRemovalSettings settings,
                              IEventAggregator events,
                              Logger logger)
        {
            _proposals = proposals;
            _flags = flags;
            _prompts = prompts;
            _requests = requests;
            _users = users;
            _access = access;
            _identities = identities;
            _backends = backends;
            _requestService = requestService;
            _feed = feed;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _audit = audit;
            _settings = settings;
            _events = events;
            _logger = logger;
        }

        private bool? _fastVotesNoticed;

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public void Execute(RemovalCheckCommand message) => TickAsync().GetAwaiter().GetResult();

        public RemovalProposal Find(int id) => _proposals.Find(id);

        public RemovalProposal LatestFor(int requestId)
        {
            var all = _proposals.ForRequest(requestId);
            return all.Where(p => p.IsOpen).OrderByDescending(p => p.Id).FirstOrDefault() ?? all.OrderByDescending(p => p.Id).FirstOrDefault();
        }

        public IReadOnlyList<RemovalFlag> FlagsOn(int proposalId) => _flags.ForProposal(proposalId);

        public async Task<LookupOutcome<RemovalTarget>> TargetAsync(int backendId, int itemId)
        {
            var instance = _backends.Find(backendId);

            if (instance == null || instance.Implementation is not (BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.TitleNotFound);
            }

            List<LibraryTitle> library;

            try
            {
                library = await _backends.ApiFor(instance).GetLibraryAsync(instance.Settings);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Reading {0}'s library failed", instance.Name);
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.RequestsPaused(DiscordText.TypeOf(instance), instance.Name));
            }

            var title = library.FirstOrDefault(t => t.Id == itemId);
            return title == null ? LookupOutcome<RemovalTarget>.Fail(DiscordText.RemovalNotInLibrary) : LookupOutcome<RemovalTarget>.Ok(TargetFor(instance, title));
        }

        public async Task<LookupOutcome<RemovalTarget>> TargetForRequestAsync(int requestId)
        {
            var request = _requests.Find(requestId);

            if (request?.BackendId == null || !request.ExternalItemId.HasValue)
            {
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.RemovalNotInLibrary);
            }

            return await TargetAsync(request.BackendId.Value, request.ExternalItemId.Value);
        }

        public async Task<List<RemovalTarget>> SearchLibraryAsync(string term)
        {
            var found = new List<RemovalTarget>();
            term = (term ?? string.Empty).Trim();

            foreach (var instance in _backends.All().Where(b => b.Enable && b.EverConnected && b.Implementation is BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                try
                {
                    var library = await _backends.ApiFor(instance).GetLibraryAsync(instance.Settings);
                    found.AddRange(library.Where(t => t.Title != null && t.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(t => TargetFor(instance, t)));
                }
                catch (BackendException ex)
                {
                    _logger.Warn(ex, "Searching {0}'s library failed", instance.Name);
                }
            }

            return found.OrderBy(t => t.Title.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Instance.Name).Take(DiscordLimits.ChoicesPerMenu).ToList();
        }

        public async Task<ServiceResult> StartByUserAsync(RequestarrUser user, Actor actor, int requestId, IReadOnlyCollection<int> seasons)
        {
            var request = _requests.Find(requestId);

            if (request == null || user == null || request.OwnerUserId != user.Id)
            {
                return ServiceResult.Fail(DiscordText.RemovalOnlyYours);
            }

            var target = await TargetForRequestAsync(requestId);

            if (target.Error != null)
            {
                return ServiceResult.Fail(target.Error);
            }

            var proposal = NewProposal(target.Value, seasons, out var error);

            if (proposal == null)
            {
                return ServiceResult.Fail(error);
            }

            proposal.InitiatedByUserId = user.Id;
            proposal.InitiatedByName = user.DisplayName;
            proposal.RequesterUserId = user.Id;
            _proposals.Insert(proposal);
            _audit.Record(actor, "removal.start", "removal", proposal.Id.ToString(), $"{Scope(proposal)} on {target.Value.Instance.Name}, by its requester");

            // Their own channel says it's up for a vote, and gets the outcome.
            var message = await PostToUser(user.Id, DiscordText.RemovalStartedByYou(proposal, Closes()));
            proposal.RequesterChannelId = message.Channel;
            proposal.RequesterMessageId = message.Id;

            await StartVoting(proposal);
            return ServiceResult.Ok(DiscordText.RemovalVoteStarted(proposal));
        }

        public async Task<ServiceResult> StartByAdminAsync(Actor admin, string adminName, int? adminUserId, int backendId, int itemId, IReadOnlyCollection<int> seasons, bool force)
        {
            var target = await TargetAsync(backendId, itemId);

            if (target.Error != null)
            {
                return ServiceResult.Fail(target.Error);
            }

            var proposal = NewProposal(target.Value, seasons, out var error);

            if (proposal == null)
            {
                return ServiceResult.Fail(error);
            }

            proposal.InitiatedByAdmin = true;
            proposal.InitiatedByUserId = adminUserId;
            proposal.InitiatedByName = adminName;
            proposal.RequesterUserId = target.Value.Requester?.Id;
            _proposals.Insert(proposal);
            _audit.Record(admin, "removal.start", "removal", proposal.Id.ToString(), $"{Scope(proposal)} on {target.Value.Instance.Name}, by the admin{(force ? ", forced" : string.Empty)}");

            if (force)
            {
                return await DeleteNow(proposal, admin, adminName, adminUserId);
            }

            if (target.Value.Requester != null)
            {
                proposal.Status = RemovalStatus.AwaitingRequester;
                proposal.AnswerBy = Clock() + _settings.RequesterTimeout;
                var message = await PostToUser(proposal.RequesterUserId.Value, DiscordText.RemovalAskRequester(proposal, proposal.AnswerBy.Value, ReminderAt(proposal)), AnswerButtons(proposal), await Card(proposal));
                proposal.RequesterChannelId = message.Channel;
                proposal.RequesterMessageId = message.Id;
                Save(proposal);
                await Changed(proposal);

                return ServiceResult.Ok(DiscordText.RemovalAskedRequester(target.Value.Requester.DisplayName, proposal.AnswerBy.Value));
            }

            // Never requested through Requestarr, or the requester was removed: straight to the vote.
            await StartVoting(proposal);
            return ServiceResult.Ok(DiscordText.RemovalVoteStarted(proposal));
        }

        public async Task<ServiceResult> AnswerAsync(int proposalId, RequestarrUser user, Actor actor, bool yes)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || user == null || proposal.RequesterUserId != user.Id)
            {
                return ServiceResult.Fail(DiscordText.RemovalNotYours);
            }

            if (proposal.Status != RemovalStatus.AwaitingRequester)
            {
                return ServiceResult.Fail(DiscordText.RemovalAlreadyMoved(proposal));
            }

            proposal.RequesterResponse = yes ? RequesterResponse.Yes : RequesterResponse.No;
            proposal.Answered = Clock();
            _audit.Record(actor, yes ? "removal.requester-yes" : "removal.requester-no", "removal", proposal.Id.ToString(), Scope(proposal));

            if (!yes)
            {
                proposal.Status = RemovalStatus.Declined;
                Save(proposal);
                await EditRequesterMessage(proposal, DiscordText.RemovalYouSaidNo(proposal));
                await Changed(proposal);
                return ServiceResult.Ok(DiscordText.RemovalYouSaidNo(proposal));
            }

            await StartVoting(proposal);
            await EditRequesterMessage(proposal, DiscordText.RemovalYouSaidYes(proposal, proposal.WindowClosesAt.Value));
            return ServiceResult.Ok(DiscordText.RemovalYouSaidYes(proposal, proposal.WindowClosesAt.Value));
        }

        public async Task<ServiceResult> SetKeepAsync(int proposalId, RequestarrUser user, Actor actor, bool keep)
        {
            var proposal = _proposals.Find(proposalId);
            var prompt = proposal == null || user == null ? null : _prompts.ForProposal(proposal.Id).FirstOrDefault(p => p.UserId == user.Id);

            if (prompt == null)
            {
                return ServiceResult.Fail(DiscordText.RemovalNotYours);
            }

            if (proposal.Status != RemovalStatus.Voting)
            {
                return ServiceResult.Fail(DiscordText.RemovalVoteClosed(proposal));
            }

            var existing = _flags.ForProposal(proposal.Id).FirstOrDefault(f => f.UserId == user.Id);

            if (keep && existing == null)
            {
                _flags.Insert(new RemovalFlag { ProposalId = proposal.Id, UserId = user.Id, FlaggedAt = Clock() });
                _audit.Record(actor, "removal.keep", "removal", proposal.Id.ToString(), Scope(proposal));
            }
            else if (!keep && existing != null)
            {
                _flags.Delete(existing);
                _audit.Record(actor, "removal.keep-withdrawn", "removal", proposal.Id.ToString(), Scope(proposal));
            }

            await _feedOrCard(proposal);
            return ServiceResult.Ok(keep ? DiscordText.RemovalKeepNoted : DiscordText.RemovalKeepTakenBack);
        }

        public async Task<ServiceResult> ForceAsync(int proposalId, Actor admin, string adminName, int? adminUserId)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || !proposal.IsOpen)
            {
                return ServiceResult.Fail(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
            }

            _audit.Record(admin, "removal.force", "removal", proposal.Id.ToString(), $"{Scope(proposal)} forced by {adminName} at {Clock():u} (was {proposal.Status}, {_flags.ForProposal(proposal.Id).Count} keep flag(s))");
            return await DeleteNow(proposal, admin, adminName, adminUserId);
        }

        public async Task<ServiceResult> DropAsync(int proposalId, Actor admin, string adminName, int? adminUserId)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || !proposal.IsOpen)
            {
                return ServiceResult.Fail(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
            }

            // Users were already told it's kept when the requester said no or the vote ended with flags.
            var toldAlready = proposal.Status is RemovalStatus.Declined or RemovalStatus.Flagged;
            proposal.Status = proposal.Status == RemovalStatus.Flagged ? RemovalStatus.Kept : RemovalStatus.Cancelled;
            proposal.Closed = Clock();
            proposal.ClosedByUserId = adminUserId;
            proposal.ClosedByName = adminName;
            Save(proposal);
            _audit.Record(admin, "removal.drop", "removal", proposal.Id.ToString(), $"{Scope(proposal)} dropped by {adminName}");

            if (!toldAlready)
            {
                await PostResults(proposal);
            }

            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDropped(proposal));
        }

        public async Task TickAsync()
        {
            await NoticeFastVotes();
            var now = Clock();

            foreach (var proposal in _proposals.WithStatus(RemovalStatus.AwaitingRequester))
            {
                if (proposal.AnswerBy <= now)
                {
                    // No answer: counts as yes, as the prompt said. They can still flag Keep it in the vote.
                    proposal.RequesterResponse = RequesterResponse.Silent;
                    proposal.Answered = now;
                    _audit.Record(Actor.System, "removal.requester-silent", "removal", proposal.Id.ToString(), Scope(proposal));
                    await StartVoting(proposal);
                    await EditRequesterMessage(proposal, DiscordText.RemovalNoAnswer(proposal, proposal.WindowClosesAt.Value));
                }
                else if (!proposal.ReminderSent && ReminderAt(proposal) is { } remind && remind <= now)
                {
                    proposal.ReminderSent = true;
                    var text = DiscordText.RemovalReminder(proposal, proposal.AnswerBy.Value);
                    var message = await Repost(proposal.RequesterChannelId, proposal.RequesterMessageId, proposal.RequesterUserId.Value, text, AnswerButtons(proposal), await Card(proposal));
                    proposal.RequesterMessageId = message ?? proposal.RequesterMessageId;
                    Save(proposal);
                }
            }

            foreach (var proposal in _proposals.WithStatus(RemovalStatus.Voting))
            {
                if (proposal.WindowClosesAt <= now)
                {
                    await CloseVote(proposal);
                }
                else
                {
                    await SyncPrompts(proposal);
                }
            }
        }

        // Dev fast votes: the admin channel says when they go on or off, so nobody takes minute-long
        // votes for the real settings. Checked with every removal check (every minute).
        private async Task NoticeFastVotes()
        {
            var on = _settings.FastVotes;

            if (_fastVotesNoticed == on || (_fastVotesNoticed == null && !on))
            {
                _fastVotesNoticed = on;
                return;
            }

            _fastVotesNoticed = on;
            _logger.Info(on ? "Dev fast votes are on: removal timings are in minutes" : "Dev fast votes are off");

            if (ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var channel))
            {
                try
                {
                    var text = on
                        ? DiscordText.FastVotesOn(DevFastVotes.VoteWindow, DevFastVotes.RequesterTimeout, DevFastVotes.Reminder, _settings.VoteDays, _settings.RequesterTimeoutDays, _settings.ReminderDay)
                        : DiscordText.FastVotesOff(_settings.VoteDays, _settings.RequesterTimeoutDays, _settings.ReminderDay);
                    await _gateway.SendMessageAsync(channel, new InteractionReply { Content = text });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not post the fast votes notice");
                }
            }
        }

        private RemovalTarget TargetFor(BackendInstance instance, LibraryTitle title)
        {
            var type = DiscordText.TypeOf(instance);
            var externalId = type == ContentType.Tv ? title.TvdbId : title.TmdbId;
            var request = externalId.HasValue
                ? _requests.ForTitle(type, externalId.Value).Where(r => r.BackendId == instance.Id && r.IsActive).Newest().FirstOrDefault()
                : null;
            var requester = request == null ? null : _users.Find(request.OwnerUserId);

            return new RemovalTarget(instance, title, request, requester?.Status == UserStatus.Removed ? null : requester);
        }

        private RemovalProposal NewProposal(RemovalTarget target, IReadOnlyCollection<int> seasons, out string error)
        {
            var scope = target.Type == ContentType.Tv ? (seasons ?? Array.Empty<int>()).Distinct().OrderBy(s => s).ToList() : new List<int>();
            error = null;

            if (scope.Except(target.Title.Seasons).Any())
            {
                error = DiscordText.RemovalNoSuchSeason;
                return null;
            }

            // One open removal per title and season scope: the whole title overlaps everything, and
            // seasons overlap when they share one; different seasons can run side by side.
            var open = _proposals.WithStatus(RemovalStatus.AwaitingRequester, RemovalStatus.Voting, RemovalStatus.Declined, RemovalStatus.Flagged, RemovalStatus.Failed)
                .FirstOrDefault(p => p.Overlaps(target.Instance.Id, target.Title.Id, scope));

            if (open != null)
            {
                error = DiscordText.RemovalAlreadyOpen(open);
                return null;
            }

            return new RemovalProposal
            {
                RequestId = target.Request?.Id,
                ContentType = target.Type,
                Title = target.Title.Title,
                Year = target.Title.Year,
                TmdbId = target.Title.TmdbId,
                TvdbId = target.Title.TvdbId,
                PosterUrl = target.Title.PosterUrl ?? target.Request?.PosterUrl,
                BackendId = target.Instance.Id,
                ExternalItemId = target.Title.Id,
                Seasons = scope,
                Status = RemovalStatus.Voting,
                Created = Clock()
            };
        }

        private async Task StartVoting(RemovalProposal proposal)
        {
            proposal.Status = RemovalStatus.Voting;
            proposal.VotingStarted = Clock();
            proposal.WindowClosesAt = Clock() + _settings.VoteWindow;
            Save(proposal);
            await SyncPrompts(proposal);
            await Changed(proposal);
        }

        // Every active user with access to the type gets the prompt, including anyone who gained
        // access since the vote opened. The one who asked for it and a requester who said yes don't.
        private async Task SyncPrompts(RemovalProposal proposal)
        {
            var prompted = _prompts.ForProposal(proposal.Id).Select(p => p.UserId).ToHashSet();
            var skip = new HashSet<int>();

            if (!proposal.InitiatedByAdmin && proposal.InitiatedByUserId.HasValue)
            {
                skip.Add(proposal.InitiatedByUserId.Value);
            }

            if (proposal.RequesterResponse == RequesterResponse.Yes && proposal.RequesterUserId.HasValue)
            {
                skip.Add(proposal.RequesterUserId.Value);
            }

            var voters = _users.All()
                .Where(u => u.Status == UserStatus.Active && u.PrivateChannelId != null && !prompted.Contains(u.Id) && !skip.Contains(u.Id))
                .Where(u => _access.GetForUser(u.Id).Any(a => a.ContentType == proposal.ContentType && a.State != AccessState.Off))
                .ToList();

            if (voters.Count == 0)
            {
                return;
            }

            var card = await Card(proposal);

            foreach (var voter in voters)
            {
                var message = await PostToUser(voter.Id, DiscordText.RemovalPrompt(proposal, proposal.WindowClosesAt.Value, false), KeepButtons(proposal, false), card);
                _prompts.Insert(new RemovalPrompt { ProposalId = proposal.Id, UserId = voter.Id, ChannelId = message.Channel, MessageId = message.Id, Sent = Clock() });
            }
        }

        private async Task CloseVote(RemovalProposal proposal)
        {
            var flags = _flags.ForProposal(proposal.Id);

            if (flags.Count == 0)
            {
                _audit.Record(Actor.System, "removal.vote-passed", "removal", proposal.Id.ToString(), $"{Scope(proposal)}: no keep flags");
                await DeleteNow(proposal, null, null, null);
                return;
            }

            // Any one flag keeps it. Users are told it's kept; the admin can still force it.
            proposal.Status = RemovalStatus.Flagged;
            proposal.Closed = Clock();
            Save(proposal);
            _audit.Record(Actor.System, "removal.kept", "removal", proposal.Id.ToString(), $"{Scope(proposal)}: {flags.Count} keep flag(s)");
            await PostResults(proposal);
            await Changed(proposal);
        }

        private async Task<ServiceResult> DeleteNow(RemovalProposal proposal, Actor admin, string adminName, int? adminUserId)
        {
            var instance = _backends.Find(proposal.BackendId ?? 0);

            try
            {
                if (instance == null)
                {
                    throw new BackendException(DiscordText.RemovalInstanceGone);
                }

                if (proposal.WholeTitle)
                {
                    await _backends.ApiFor(instance).DeleteItemAsync(instance.Settings, proposal.ExternalItemId.Value);
                }
                else
                {
                    await ((ISonarrApi)_backends.ApiFor(instance)).DeleteSeasonsAsync(instance.Settings, proposal.ExternalItemId.Value, proposal.Seasons);
                }
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Removing {0} from {1} failed", proposal.Label, instance?.Name);
                proposal.Status = RemovalStatus.Failed;
                proposal.Detail = ex.Message;
                proposal.Closed = Clock();
                Save(proposal);
                _audit.Record(admin ?? Actor.System, "removal.failed", "removal", proposal.Id.ToString(), ex.Message);
                await Changed(proposal);
                return ServiceResult.Fail(DiscordText.RemovalFailed(proposal, instance?.Name));
            }

            proposal.Status = admin != null ? RemovalStatus.Forced : RemovalStatus.Deleted;
            proposal.Closed = Clock();
            proposal.ClosedByUserId = adminUserId;
            proposal.ClosedByName = adminName;
            Save(proposal);
            var how = admin != null ? $", forced by {adminName}" : ", by vote";
            _audit.Record(admin ?? Actor.System, "removal.deleted", "removal", proposal.Id.ToString(), $"{Scope(proposal)} deleted from {instance.Name} with its files, not excluded{how}");

            // The requests for it are closed (whole title) or lose those seasons; a later request is fresh.
            foreach (var request in _requests.ForTitle(proposal.ContentType, proposal.ExternalId ?? 0).Where(r => r.BackendId == proposal.BackendId && r.IsActive).ToList())
            {
                await _requestService.ApplyRemovalAsync(request, proposal.Seasons, DiscordText.RemovalNote(proposal));
            }

            await PostResults(proposal);
            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDone(proposal, instance.Name));
        }

        // Everyone prompted, and the requester, get their message reposted with the outcome, no buttons.
        private async Task PostResults(RemovalProposal proposal)
        {
            var outcome = DiscordText.RemovalOutcome(proposal);
            var card = await Card(proposal);

            foreach (var prompt in _prompts.ForProposal(proposal.Id))
            {
                var id = await Repost(prompt.ChannelId, prompt.MessageId, prompt.UserId, outcome, null, card);
                prompt.MessageId = id ?? prompt.MessageId;
                _prompts.Update(prompt);
            }

            if (proposal.RequesterUserId.HasValue && proposal.RequesterMessageId != null && _prompts.ForProposal(proposal.Id).All(p => p.UserId != proposal.RequesterUserId))
            {
                // Deleted: their request message is reposted with the outcome, so this one just goes.
                var requestReposted = proposal.Status is RemovalStatus.Deleted or RemovalStatus.Forced && proposal.RequestId.HasValue &&
                                      _requests.Find(proposal.RequestId.Value)?.OwnerUserId == proposal.RequesterUserId;

                if (requestReposted && ulong.TryParse(proposal.RequesterChannelId, out var channel) && ulong.TryParse(proposal.RequesterMessageId, out var message))
                {
                    await _gateway.DeleteMessageAsync(channel, message);
                }
                else
                {
                    proposal.RequesterMessageId = await Repost(proposal.RequesterChannelId, proposal.RequesterMessageId, proposal.RequesterUserId.Value, outcome, null, card) ?? proposal.RequesterMessageId;
                    Save(proposal);
                }
            }
        }

        // The feed follows every step (the request's card, or the removal's own for ownerless titles),
        // and /admin requests may have gained or lost an item.
        private async Task Changed(RemovalProposal proposal)
        {
            await _feedOrCard(proposal);
            _events.PublishEvent(new AdminQueueChangedEvent());
        }

        private async Task _feedOrCard(RemovalProposal proposal)
        {
            var request = proposal.RequestId.HasValue ? _requests.Find(proposal.RequestId.Value) : null;

            if (request != null)
            {
                await _feed.PostAsync(request);

                // Once removed, the request message was just reposted with the outcome.
                if (proposal.Status is not (RemovalStatus.Deleted or RemovalStatus.Forced))
                {
                    await _requestService.RefreshOwnerMessageAsync(request);
                }
            }
            else
            {
                await _feed.PostRemovalAsync(proposal);
            }
        }

        private async Task EditRequesterMessage(RemovalProposal proposal, string content)
        {
            if (!ulong.TryParse(proposal.RequesterChannelId, out var channel) || !ulong.TryParse(proposal.RequesterMessageId, out var message))
            {
                return;
            }

            try
            {
                await _gateway.EditMessageAsync(channel, message, new InteractionReply { Content = $"{Mention(proposal.RequesterUserId.Value)} {content}", KeepEmbeds = true });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Removal {0}: the requester's message could not be edited", proposal.Id);
            }
        }

        private async Task<(string Channel, string Id)> PostToUser(int userId, string content, List<InteractionButton> buttons = null, InteractionEmbed card = null)
        {
            var user = _users.Find(userId);

            if (user?.Status != UserStatus.Active || !ulong.TryParse(user.PrivateChannelId, out var channel))
            {
                return (null, null);
            }

            try
            {
                var id = await _gateway.SendMessageAsync(channel, Message(userId, content, buttons, card));
                return (channel.ToString(), id.ToString());
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post a removal message to user {0}", userId);
                return (null, null);
            }
        }

        // Deletes the old message and posts the new one at the bottom (the post is the notification).
        private async Task<string> Repost(string channelId, string messageId, int userId, string content, List<InteractionButton> buttons, InteractionEmbed card)
        {
            if (!ulong.TryParse(channelId, out var channel))
            {
                return null;
            }

            try
            {
                if (ulong.TryParse(messageId, out var old) && !await _gateway.DeleteMessageAsync(channel, old))
                {
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { Content = content, KeepEmbeds = true });
                }

                return (await _gateway.SendMessageAsync(channel, Message(userId, content, buttons, card))).ToString();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Removal message for user {0} could not be reposted", userId);
                return null;
            }
        }

        private InteractionReply Message(int userId, string content, List<InteractionButton> buttons, InteractionEmbed card) => new()
        {
            Content = DiscordLimits.Truncate($"{Mention(userId)} {content}".Trim(), DiscordLimits.MessageLength),
            Embeds = card == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { card },
            Buttons = buttons ?? new List<InteractionButton>()
        };

        private async Task<InteractionEmbed> Card(RemovalProposal proposal)
        {
            var instance = _backends.Find(proposal.BackendId ?? 0);
            var item = instance == null || !proposal.ExternalId.HasValue ? null : await _requestService.LookupItemAsync(instance, proposal.ContentType, proposal.ExternalId.Value);

            return item == null
                ? new InteractionEmbed { Title = proposal.Label, ThumbnailUrl = proposal.PosterUrl, Color = RequestCards.CancelledColor }
                : RequestCards.Card(proposal.ContentType, item, instance.Name, RequestCards.CancelledColor, compact: true);
        }

        private DateTime? ReminderAt(RemovalProposal proposal) =>
            _settings.ReminderAfter is { } after ? proposal.Created + after : null;

        private DateTime Closes() => Clock() + _settings.VoteWindow;

        private static List<InteractionButton> AnswerButtons(RemovalProposal proposal) => new()
        {
            new() { CustomId = $"{RemovalRoutes.Answer}:{proposal.Id}:y", Label = DiscordText.RemovalYesLabel, Style = ButtonStyle.Danger },
            new() { CustomId = $"{RemovalRoutes.Answer}:{proposal.Id}:n", Label = DiscordText.RemovalNoLabel, Style = ButtonStyle.Success }
        };

        public static List<InteractionButton> KeepButtons(RemovalProposal proposal, bool flagged) => new()
        {
            flagged
                ? new() { CustomId = $"{RemovalRoutes.Unkeep}:{proposal.Id}", Label = DiscordText.RemovalTakeBackLabel, Style = ButtonStyle.Secondary }
                : new() { CustomId = $"{RemovalRoutes.Keep}:{proposal.Id}", Label = DiscordText.RemovalKeepLabel, Style = ButtonStyle.Success }
        };

        private string Mention(int userId)
        {
            var discordId = _identities.GetForUser(userId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;
            return discordId == null ? string.Empty : $"<@{discordId}>";
        }

        private static string Scope(RemovalProposal proposal) => $"{proposal.Label}{(proposal.WholeTitle ? string.Empty : $" seasons {string.Join(", ", proposal.Seasons)}")}";

        private void Save(RemovalProposal proposal) => _proposals.Update(proposal);
    }
}
