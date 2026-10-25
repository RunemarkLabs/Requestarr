using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Access
{
    // Custom IDs of the welcome channel's buttons and the private form they open. {types}: "movie,tv"; {media}: a
    // media server ("plex") or empty.
    public static class WelcomeRoutes
    {
        public const string Request = "welcome-request";       // the Request access button
        public const string Media = "welcome-media";           // welcome-media:{provider}: one per connected media server
        public const string Types = "welcome-types";           // the form's menu of content types (and media servers)
        public const string Continue = "welcome-next";         // welcome-next:{types}:{media} -> the note form
        public const string Form = "welcome-form";             // welcome-form:{types}:{media}:{f = from the form | d = from the welcome message}
        public const string MediaForm = "welcome-media-form";  // welcome-media-form:{provider}
        public const string NoteField = "note";
        public const string UsernameField = "username";
    }

    // Custom IDs on the admin side: the access request card (admin channel and /admin requests) and Unblock.
    public static class AccessRoutes
    {
        public const string Approve = "areq-approve";      // areq-approve:{id}: choose the access levels
        public const string Level = "areq-level";          // areq-level:{id}:{levels}:{type}, value the level
        public const string Confirm = "areq-ok";           // areq-ok:{id}:{levels}
        public const string Deny = "areq-deny";            // areq-deny:{id} -> reason form
        public const string DenyForm = "areq-deny-form";   // areq-deny-form:{id}
        public const string Unblock = "adm-unblock";       // adm-unblock:{d}
        public const string ReasonField = "reason";
    }

    // What a welcome button click leads to: the form, or a private answer and nothing else.
    public record WelcomeAnswer(bool OpenForm, string Message)
    {
        public static WelcomeAnswer Form() => new(true, null);

        public static WelcomeAnswer Say(string message) => new(false, message);
    }

    public interface IAccessRequestService
    {
        // Content types someone can ask for: those with an instance that has connected at least once.
        IReadOnlyList<ContentType> OfferedTypes();

        // A click on a welcome button. Clicks that can't do anything count towards the spam guard.
        Task<WelcomeAnswer> ClickAsync(ulong discordUserId, bool isAdmin, MediaServerProvider? media = null);

        Task<ServiceResult> SubmitAsync(ulong discordUserId, string username, string displayName, IReadOnlyList<ContentType> types, string note, MediaServerProvider? media, string mediaUsername);
        Task<ServiceResult> SubmitMediaAsync(ulong discordUserId, string username, string displayName, MediaServerProvider provider, string mediaUsername);

        Task<ServiceResult> ApproveAsync(int requestId, IDictionary<ContentType, AccessState> levels, Actor admin, string adminName);
        Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName);

        AccessRequest Find(int requestId);
        AccessRequest PendingFor(ulong discordUserId);
        InteractionReply AdminCard(AccessRequest request);

        bool IsBlocked(ulong discordUserId);
        Task<ServiceResult> UnblockAsync(ulong discordUserId, Actor actor);

        // A member's roles changed (or the bot started): someone whose blocked role was removed by hand is unblocked.
        Task MemberRolesChangedAsync(ulong discordUserId, IReadOnlyList<ulong> roleIds);
        Task ReconcileBlocksAsync();
    }

    public class AccessRequestService : IAccessRequestService
    {
        private readonly IAccessRequestRepository _requests;
        private readonly IWelcomeGuardRepository _guards;
        private readonly IAccessSettings _settings;
        private readonly IUserManagementService _users;
        private readonly IMediaServerAccessRequestRepository _mediaRequests;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly IMediaServerConnections _mediaServers;
        private readonly IBackendService _backends;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IAuditService _audit;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public AccessRequestService(IAccessRequestRepository requests,
                                    IWelcomeGuardRepository guards,
                                    IAccessSettings settings,
                                    IUserManagementService users,
                                    IMediaServerAccessRequestRepository mediaRequests,
                                    IMediaServerAccountRepository accounts,
                                    IMediaServerConnections mediaServers,
                                    IBackendService backends,
                                    IDiscordGuildGateway gateway,
                                    IGuildSetupService guildSetup,
                                    IAuditService audit,
                                    IEventAggregator events,
                                    Logger logger)
        {
            _requests = requests;
            _guards = guards;
            _settings = settings;
            _users = users;
            _mediaRequests = mediaRequests;
            _accounts = accounts;
            _mediaServers = mediaServers;
            _backends = backends;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _audit = audit;
            _events = events;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public IReadOnlyList<ContentType> OfferedTypes() => ContentTypes.Requestable.Where(_backends.EverConnected).ToList();

        public AccessRequest Find(int requestId) => _requests.Find(requestId);

        public AccessRequest PendingFor(ulong discordUserId) => _requests.ForMember(discordUserId.ToString()).LastOrDefault(r => r.Status == AccessRequestState.Pending);

        public bool IsBlocked(ulong discordUserId) => _guards.Find(discordUserId.ToString())?.Blocked != null;

        public async Task<WelcomeAnswer> ClickAsync(ulong discordUserId, bool isAdmin, MediaServerProvider? media = null)
        {
            var now = Clock();
            var guard = _guards.Find(discordUserId.ToString());

            if (guard?.Blocked != null)
            {
                return WelcomeAnswer.Say(DiscordText.WelcomeBlocked);
            }

            if (isAdmin && media == null)
            {
                return WelcomeAnswer.Say(DiscordText.WelcomeAdmin);
            }

            // A spam cooldown: every click is answered with the time left and does nothing else.
            if (SpamRules.CooldownLeft(guard, now) is { } left)
            {
                return await CountAsync(discordUserId, guard, now, DiscordText.WelcomeSlowDown(left));
            }

            var status = Status(discordUserId, media, now);

            return status == null ? WelcomeAnswer.Form() : await CountAsync(discordUserId, guard, now, status);
        }

        public async Task<ServiceResult> SubmitAsync(ulong discordUserId, string username, string displayName, IReadOnlyList<ContentType> types, string note, MediaServerProvider? media, string mediaUsername)
        {
            // Checked again: the form may have stayed open while something changed.
            if (IsBlocked(discordUserId))
            {
                return ServiceResult.Fail(DiscordText.WelcomeBlocked);
            }

            if (Status(discordUserId, null, Clock()) is { } status)
            {
                return ServiceResult.Fail(status);
            }

            if (media.HasValue && !ValidUsername(mediaUsername))
            {
                return ServiceResult.Fail(DiscordText.InvalidUsername);
            }

            var request = _requests.Insert(new AccessRequest
            {
                DiscordUserId = discordUserId.ToString(),
                Username = username,
                DisplayName = displayName ?? username,
                ContentTypes = types.Where(t => OfferedTypes().Contains(t)).Distinct().OrderBy(t => t).ToList(),
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                MediaProvider = media,
                MediaUsername = media.HasValue ? mediaUsername.Trim() : null,
                Status = AccessRequestState.Pending,
                Requested = Clock()
            });

            if (ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel))
            {
                try
                {
                    request.CardMessageId = (await _gateway.SendMessageAsync(adminChannel, AdminCard(request))).ToString();
                    _requests.Update(request);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not post the access request card for {0}", request.DisplayName);
                }
            }

            var detail = string.Join(", ", request.ContentTypes) + (media.HasValue ? $"; {media} {request.MediaUsername}" : string.Empty);
            _audit.Record(Actor.Discord(discordUserId, username, null), "access.request", "access-request", request.Id.ToString(), detail);
            _events.PublishEvent(new AdminQueueChangedEvent());

            return ServiceResult.Ok(DiscordText.AccessRequestSentToAdmin);
        }

        public async Task<ServiceResult> SubmitMediaAsync(ulong discordUserId, string username, string displayName, MediaServerProvider provider, string mediaUsername)
        {
            if (IsBlocked(discordUserId))
            {
                return ServiceResult.Fail(DiscordText.WelcomeBlocked);
            }

            if (!_mediaServers.IsConnected(provider))
            {
                return ServiceResult.Fail(DiscordText.MediaServerNotConnected(provider));
            }

            if (Status(discordUserId, provider, Clock()) is { } status)
            {
                return ServiceResult.Fail(status);
            }

            // Someone who isn't a member yet gets a record without a channel, like an admin who requests; the link
            // request then goes to the admin exactly as Link Plex does.
            var user = _users.EnsureRecord(discordUserId, username, displayName);

            return await _users.RequestMediaServerAccessAsync(user, discordUserId, provider, mediaUsername);
        }

        public async Task<ServiceResult> ApproveAsync(int requestId, IDictionary<ContentType, AccessState> levels, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestNotFound);
            }

            if (request.Status != AccessRequestState.Pending)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestAlreadyDecided);
            }

            var discordId = ulong.Parse(request.DiscordUserId);
            var existing = _users.FindByDiscordId(discordId);
            var alreadyIn = existing != null && existing.Status != UserStatus.Removed && existing.PrivateChannelId != null;

            if (!alreadyIn)
            {
                // Exactly what adding from the admin panel does: role, private channel, the chosen access.
                var added = await _users.AddAsync(discordId, levels, admin);

                if (!added.Success)
                {
                    return added;
                }
            }

            request.Status = AccessRequestState.Approved;
            request.Decided = Clock();
            request.DecidedByUserId = admin.UserId;
            request.DecidedByName = adminName;
            _requests.Update(request);

            var user = _users.FindByDiscordId(discordId);

            if (request.MediaProvider is { } provider && user != null && !_accounts.GetForUser(user.Id).Any(a => a.Provider == provider))
            {
                await _users.RequestMediaServerAccessAsync(user, discordId, provider, request.MediaUsername);
            }

            var outcome = DiscordText.AccessRequestApprovedCard(request, adminName, user?.PrivateChannelId);
            await CloseCardAsync(request, outcome);
            await TellAsync(discordId, DiscordText.AccessRequestApprovedMember(user?.PrivateChannelId));

            _audit.Record(admin, "access.approve", "access-request", request.Id.ToString(), alreadyIn ? "already a member" : string.Join(", ", levels.Select(l => $"{l.Key}={l.Value}")));
            _events.PublishEvent(new AdminQueueChangedEvent());

            return ServiceResult.Ok(outcome);
        }

        public async Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestNotFound);
            }

            if (request.Status != AccessRequestState.Pending)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestAlreadyDecided);
            }

            request.Status = AccessRequestState.Denied;
            request.Decided = Clock();
            request.DecidedByUserId = admin.UserId;
            request.DecidedByName = adminName;
            request.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            _requests.Update(request);

            var outcome = DiscordText.AccessRequestDeniedCard(request, adminName);
            await CloseCardAsync(request, outcome);
            await TellAsync(ulong.Parse(request.DiscordUserId), DiscordText.AccessRequestDeniedMember(request.Reason, AskAgainFrom(request)));

            _audit.Record(admin, "access.deny", "access-request", request.Id.ToString(), request.Reason);
            _events.PublishEvent(new AdminQueueChangedEvent());

            return ServiceResult.Ok(outcome);
        }

        public InteractionReply AdminCard(AccessRequest request) => new()
        {
            Content = DiscordText.AccessRequestAdminCard(request),
            Buttons = new List<InteractionButton>
            {
                new() { CustomId = $"{AccessRoutes.Approve}:{request.Id}", Label = DiscordText.ApproveAccessLabel, Style = ButtonStyle.Success },
                new() { CustomId = $"{AccessRoutes.Deny}:{request.Id}", Label = DiscordText.DenyLabel, Style = ButtonStyle.Danger }
            }
        };

        public async Task<ServiceResult> UnblockAsync(ulong discordUserId, Actor actor)
        {
            var guard = _guards.Find(discordUserId.ToString());

            if (guard?.Blocked == null)
            {
                return ServiceResult.Fail(DiscordText.NotBlocked);
            }

            Clear(guard);

            if (_guildSetup.BlockedRoleId is { } role)
            {
                try
                {
                    await _gateway.RemoveRoleFromMemberAsync(discordUserId, role);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not take the blocked role from {0}", discordUserId);
                }
            }

            _audit.Record(actor, "access.unblock", "member", discordUserId.ToString());

            return ServiceResult.Ok(DiscordText.Unblocked(discordUserId));
        }

        public Task MemberRolesChangedAsync(ulong discordUserId, IReadOnlyList<ulong> roleIds)
        {
            var guard = _guards.Find(discordUserId.ToString());

            if (guard?.Blocked != null && _guildSetup.BlockedRoleId is { } role && !roleIds.Contains(role))
            {
                Clear(guard);
                _audit.Record(Actor.System, "access.unblock", "member", discordUserId.ToString(), "the blocked role was removed in Discord");
                _logger.Info("Member {0} is unblocked: their blocked role was removed in Discord", discordUserId);
            }

            return Task.CompletedTask;
        }

        public async Task ReconcileBlocksAsync()
        {
            foreach (var guard in _guards.AllBlocked())
            {
                if (ulong.TryParse(guard.DiscordUserId, out var id) && await _gateway.GetMemberAsync(id) is { } member)
                {
                    await MemberRolesChangedAsync(id, member.RoleIds);
                }
            }
        }

        // Why this click can't open a form, or null when it can.
        private string Status(ulong discordUserId, MediaServerProvider? media, DateTime now)
        {
            var user = _users.FindByDiscordId(discordUserId);
            var member = user != null && user.Status != UserStatus.Removed && user.PrivateChannelId != null;

            if (user?.Status == UserStatus.Suspended && user.PrivateChannelId != null)
            {
                return DiscordText.WelcomeSuspended;
            }

            if (media is { } provider)
            {
                if (user != null && _accounts.GetForUser(user.Id).Any(a => a.Provider == provider))
                {
                    return DiscordText.WelcomeMediaLinked(provider);
                }

                if (user != null && _mediaRequests.GetPendingForUser(user.Id).Any(r => r.Provider == provider))
                {
                    return DiscordText.AccessRequestAlreadyPending;
                }

                if (PendingFor(discordUserId) is { MediaProvider: { } asked } open && asked == provider)
                {
                    return DiscordText.WelcomePending(open.Requested);
                }

                return null;
            }

            if (member)
            {
                return DiscordText.WelcomeAlreadyIn(user.PrivateChannelId);
            }

            if (PendingFor(discordUserId) is { } pending)
            {
                return DiscordText.WelcomePending(pending.Requested);
            }

            var last = _requests.ForMember(discordUserId.ToString()).LastOrDefault();

            if (last?.Status == AccessRequestState.Denied && AskAgainFrom(last) is { } from && from > now)
            {
                return DiscordText.WelcomeDeniedWait(last.Reason, from);
            }

            return null;
        }

        private DateTime? AskAgainFrom(AccessRequest denied) =>
            _settings.DenialCooldownDays > 0 && denied.Decided.HasValue ? denied.Decided.Value.AddDays(_settings.DenialCooldownDays) : null;

        // A click that couldn't do anything: one row per person, rewritten, never a message to the admin, except the
        // one notice when it blocks them.
        private async Task<WelcomeAnswer> CountAsync(ulong discordUserId, WelcomeGuard guard, DateTime now, string status)
        {
            guard ??= new WelcomeGuard { DiscordUserId = discordUserId.ToString() };
            var outcome = SpamRules.Count(guard, now, _settings);
            _guards.Upsert(guard);

            switch (outcome)
            {
                case GuardOutcome.Blocked:
                    await BlockAsync(discordUserId);
                    return WelcomeAnswer.Say(DiscordText.WelcomeBlockedNow);

                case GuardOutcome.ShortCooldown:
                case GuardOutcome.LongCooldown:
                    _logger.Info("Welcome buttons: member {0} is waiting {1} after repeated clicks", discordUserId, guard.CooldownUntil - now);
                    return WelcomeAnswer.Say(status + "\n" + DiscordText.WelcomeSlowDown(guard.CooldownUntil.Value - now));

                default:
                    return WelcomeAnswer.Say(status);
            }
        }

        private async Task BlockAsync(ulong discordUserId)
        {
            _logger.Info("Member {0} is blocked from the welcome buttons after repeated clicks", discordUserId);
            _audit.Record(Actor.System, "access.block", "member", discordUserId.ToString(), "repeated clicks on the welcome buttons");

            try
            {
                if (_guildSetup.BlockedRoleId is { } role)
                {
                    await _gateway.AddRoleToMemberAsync(discordUserId, role);
                }

                if (ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel))
                {
                    var member = await _gateway.GetMemberAsync(discordUserId);
                    await _gateway.SendMessageAsync(adminChannel, new InteractionReply
                    {
                        Content = DiscordText.BlockedAdminNotice(discordUserId, member?.DisplayName),
                        Buttons = new List<InteractionButton> { new() { CustomId = $"{AccessRoutes.Unblock}:{discordUserId}", Label = DiscordText.UnblockLabel, Style = ButtonStyle.Secondary } }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not finish blocking {0} in Discord", discordUserId);
            }
        }

        private void Clear(WelcomeGuard guard)
        {
            guard.Blocked = null;
            guard.CooldownUntil = null;
            guard.LongCooldownStarted = null;
            guard.ClicksSinceLong = 0;
            guard.RecentClicks = null;
            _guards.Update(guard);
        }

        // The card in the admin channel shows the outcome and loses its buttons.
        private async Task CloseCardAsync(AccessRequest request, string outcome)
        {
            if (!ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel) || !ulong.TryParse(request.CardMessageId, out var card))
            {
                return;
            }

            try
            {
                await _gateway.EditMessageAsync(adminChannel, card, new InteractionReply { Content = DiscordText.AccessRequestAdminCard(request) + "\n" + outcome });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not update the access request card {0}", request.Id);
            }
        }

        // The outcome reaches the person privately. If their settings refuse messages from the server, they see it
        // the next time they press the button.
        private async Task TellAsync(ulong discordUserId, string message)
        {
            try
            {
                if (!await _gateway.SendDirectMessageAsync(discordUserId, new InteractionReply { Content = message }))
                {
                    _logger.Info("Member {0} doesn't accept private messages; they'll see the outcome on the welcome button", discordUserId);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not send member {0} the outcome of their access request", discordUserId);
            }
        }

        private static bool ValidUsername(string username) => !string.IsNullOrWhiteSpace(username) && username.Trim().Length <= 100;
    }
}
