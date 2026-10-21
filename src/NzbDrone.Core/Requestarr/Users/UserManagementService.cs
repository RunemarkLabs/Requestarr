using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;

namespace Requestarr.Core.Users
{
    public record ServiceResult(bool Success, string Message)
    {
        public static ServiceResult Ok(string message) => new(true, message);

        public static ServiceResult Fail(string message) => new(false, message);
    }

    public interface IUserManagementService
    {
        Task<ServiceResult> AddAsync(ulong discordUserId, IDictionary<ContentType, AccessState> access, Actor actor);
        Task<ServiceResult> RemoveAsync(ulong discordUserId, Actor actor);
        Task<ServiceResult> SuspendAsync(ulong discordUserId, Actor actor, string reason = null);
        Task<ServiceResult> UnsuspendAsync(ulong discordUserId, Actor actor);
        ServiceResult SetAccess(ulong discordUserId, ContentType type, AccessState state, Actor actor, string instanceName = null);
        RequestarrUser EnsureAdminUser(ulong discordUserId, string username, string displayName);
        ServiceResult SetQuota(ulong discordUserId, ContentType type, int limit, int days, Actor actor);
        ServiceResult Info(ulong discordUserId);
        Task HandleMemberLeftAsync(ulong discordUserId);
        ServiceResult LinkMediaServer(ulong discordUserId, MediaServerProvider provider, string username, Actor actor);
        Task<ServiceResult> RequestMediaServerAccessAsync(RequestarrUser user, ulong discordUserId, MediaServerProvider provider, string username);
        Task<ServiceResult> DecideAccessRequestAsync(int requestId, bool approve, Actor admin, string adminName);
        RequestarrUser FindByDiscordId(ulong discordUserId);
    }

    // All user rules live here so Discord, the web portal, and the API behave the same.
    public class UserManagementService : IUserManagementService
    {
        public static readonly IReadOnlyDictionary<ContentType, AccessState> DefaultAccess = new Dictionary<ContentType, AccessState>
        {
            { ContentType.Movie, AccessState.NeedsApproval },
            { ContentType.Tv, AccessState.NeedsApproval },
            { ContentType.Music, AccessState.Off },
            { ContentType.Comic, AccessState.Off },
            { ContentType.Book, AccessState.Off }
        };

        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly IMediaServerAccessRequestRepository _accessRequests;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IBackendService _backends;
        private readonly IAuditService _audit;
        private readonly IPanelService _panels;
        private readonly Logger _logger;

        public UserManagementService(IRequestarrUserRepository users,
                                     IUserIdentityRepository identities,
                                     IUserMediaAccessRepository access,
                                     IMediaServerAccountRepository accounts,
                                     IMediaServerAccessRequestRepository accessRequests,
                                     IDiscordGuildGateway gateway,
                                     IGuildSetupService guildSetup,
                                     IBackendService backends,
                                     IAuditService audit,
                                     IPanelService panels,
                                     Logger logger)
        {
            _users = users;
            _identities = identities;
            _access = access;
            _accounts = accounts;
            _accessRequests = accessRequests;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _backends = backends;
            _audit = audit;
            _panels = panels;
            _logger = logger;
        }

        public RequestarrUser FindByDiscordId(ulong discordUserId)
        {
            var identity = _identities.Find(UserIdentity.DiscordProvider, discordUserId.ToString());

            return identity == null ? null : _users.Find(identity.UserId);
        }

        public async Task<ServiceResult> AddAsync(ulong discordUserId, IDictionary<ContentType, AccessState> access, Actor actor)
        {
            var member = await _gateway.GetMemberAsync(discordUserId);

            if (member == null)
            {
                return ServiceResult.Fail(DiscordText.MemberNotFound);
            }

            if (member.IsBot)
            {
                return ServiceResult.Fail(DiscordText.CannotAddBot);
            }

            var identity = _identities.Find(UserIdentity.DiscordProvider, discordUserId.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            // An admin's own record (created when they request from the admin channel) has no channel yet.
            if (user != null && user.Status != UserStatus.Removed && user.PrivateChannelId != null)
            {
                return ServiceResult.Fail(DiscordText.AlreadyUser(member.DisplayName, user.Status));
            }

            if (await _gateway.GetRoleCountAsync() >= DiscordLimits.RolesPerServer - DiscordLimits.RoleHeadroom)
            {
                return ServiceResult.Fail(DiscordText.RoleLimit);
            }

            ulong? roleId = null;
            ulong? channelId = null;

            try
            {
                roleId = await _gateway.CreateRoleAsync(ChannelLayout.UserRoleName(member.Username));
                await _gateway.AddRoleToMemberAsync(member.Id, roleId.Value);
                await SetMembership(member.Id, true);

                var categoryId = await _guildSetup.GetCategoryWithRoomAsync();
                var overwrites = ChannelLayout.UserChannel(_gateway.GuildId, _gateway.BotUserId, _guildSetup.AdminRoleId, roleId.Value, locked: false);
                channelId = await _gateway.CreateTextChannelAsync(ChannelLayout.UserChannelName(member.Username), categoryId, $"Requestarr requests for {member.DisplayName}", overwrites);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Adding Discord member {0} failed, rolling back", discordUserId);
                await RollBack(roleId, channelId);

                return ServiceResult.Fail(DiscordText.AddFailed);
            }

            var now = DateTime.UtcNow;
            user ??= new RequestarrUser { Added = now };
            user.DisplayName = member.DisplayName;
            user.Status = UserStatus.Active;
            user.StatusChanged = now;
            user.DiscordRoleId = roleId.ToString();
            user.PrivateChannelId = channelId.ToString();
            _users.Upsert(user);

            identity ??= new UserIdentity { Provider = UserIdentity.DiscordProvider, ExternalId = discordUserId.ToString(), Linked = now };
            identity.UserId = user.Id;
            identity.Username = member.Username;
            identity.DisplayName = member.DisplayName;
            identity.AvatarUrl = member.AvatarUrl;
            _identities.Upsert(identity);

            var rows = SaveAccess(user.Id, access);

            // The pinned panel is the welcome: one message, kept in step with the user's access.
            await _panels.RefreshUserPanelAsync(user);

            var accessSummary = string.Join(", ", rows.Where(r => ContentTypes.IsOffered(r.ContentType)).Select(r => $"{r.ContentType}={r.State}"));
            _audit.Record(actor, "user.add", "user", user.Id.ToString(), $"discord {discordUserId}, role {roleId}, channel {channelId}, access {accessSummary}");

            return ServiceResult.Ok(DiscordText.UserAdded(member.DisplayName, channelId.Value, rows));
        }

        public async Task<ServiceResult> RemoveAsync(ulong discordUserId, Actor actor)
        {
            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            if (user.PrivateChannelId != null && ulong.TryParse(user.PrivateChannelId, out var channelId) && await _gateway.ChannelExistsAsync(channelId))
            {
                await _gateway.SetChannelOverwritesAsync(channelId, ChannelLayout.AdminOnly(_gateway.GuildId, _gateway.BotUserId, _guildSetup.AdminRoleId));
                await _gateway.RenameChannelAsync(channelId, ChannelLayout.ArchivedName(ChannelLayout.UserChannelName(Identity(user)?.Username ?? user.DisplayName)));
            }

            if (ulong.TryParse(user.DiscordRoleId, out var roleId))
            {
                await _gateway.DeleteRoleAsync(roleId);
            }

            await SetMembership(discordUserId, false);

            user.Status = UserStatus.Removed;
            user.StatusChanged = DateTime.UtcNow;
            user.DiscordRoleId = null;
            _users.Update(user);

            _audit.Record(actor, "user.remove", "user", user.Id.ToString(), $"channel {user.PrivateChannelId} archived, role {roleId} deleted");

            return ServiceResult.Ok(DiscordText.UserRemovedDone(user.DisplayName));
        }

        public async Task<ServiceResult> SuspendAsync(ulong discordUserId, Actor actor, string reason = null)
        {
            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            if (user.Status == UserStatus.Suspended)
            {
                return ServiceResult.Fail(DiscordText.AlreadySuspended(user.DisplayName));
            }

            await SetChannelLock(user, locked: true, reason == null ? DiscordText.ChannelLockedNotice : DiscordText.MemberLeftNotice);
            await SetMembership(discordUserId, false);

            user.Status = UserStatus.Suspended;
            user.StatusChanged = DateTime.UtcNow;
            _users.Update(user);

            _audit.Record(actor, reason == null ? "user.suspend" : "user.departed", "user", user.Id.ToString(), reason);

            return ServiceResult.Ok(DiscordText.UserSuspendedDone(user.DisplayName));
        }

        public async Task<ServiceResult> UnsuspendAsync(ulong discordUserId, Actor actor)
        {
            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            if (user.Status != UserStatus.Suspended)
            {
                return ServiceResult.Fail(DiscordText.NotSuspended(user.DisplayName));
            }

            var member = await _gateway.GetMemberAsync(discordUserId);

            if (member == null)
            {
                return ServiceResult.Fail(DiscordText.NotInServer);
            }

            // A member who left and rejoined lost their role; give it back.
            if (ulong.TryParse(user.DiscordRoleId, out var roleId) && !member.RoleIds.Contains(roleId))
            {
                await _gateway.AddRoleToMemberAsync(member.Id, roleId);
            }

            await SetChannelLock(user, locked: false, DiscordText.ChannelRestoredNotice);
            await SetMembership(discordUserId, true);

            user.Status = UserStatus.Active;
            user.StatusChanged = DateTime.UtcNow;
            _users.Update(user);

            _audit.Record(actor, "user.unsuspend", "user", user.Id.ToString());

            return ServiceResult.Ok(DiscordText.UserUnsuspendedDone(user.DisplayName));
        }

        public ServiceResult SetAccess(ulong discordUserId, ContentType type, AccessState state, Actor actor, string instanceName = null)
        {
            if (!ContentTypes.IsOffered(type))
            {
                return ServiceResult.Fail(DiscordText.TypeNotOffered);
            }

            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            BackendInstance instance = null;

            if (instanceName.IsNotNullOrWhiteSpace())
            {
                instance = _backends.FindByName(instanceName.Trim());

                if (instance == null || !instance.Enable || instance.Implementation != BackendImplementations.ForContentType(type))
                {
                    return ServiceResult.Fail(DiscordText.InstanceNotFound(instanceName.Trim(), type));
                }
            }

            var row = GetOrCreateAccess(user.Id, type);
            var previous = row.State;
            row.State = state;

            if (instance != null)
            {
                row.BackendId = instance.Id;
            }

            _access.Upsert(row);

            _audit.Record(actor, "user.access", "user", user.Id.ToString(), $"{type}: {previous} -> {state}{(instance != null ? $", instance {instance.Name}" : string.Empty)}");
            _panels.RefreshUserPanelAsync(user).GetAwaiter().GetResult();

            return ServiceResult.Ok(DiscordText.AccessChanged(user.DisplayName, type, state, instance?.Name));
        }

        // Admins may request from the admin channel without having been added; they get a
        // Requestarr user record (no role, no channel) so requests have an owner.
        public RequestarrUser EnsureAdminUser(ulong discordUserId, string username, string displayName)
        {
            var existing = FindByDiscordId(discordUserId);

            if (existing != null)
            {
                return existing;
            }

            var now = DateTime.UtcNow;
            var user = _users.Insert(new RequestarrUser { DisplayName = displayName ?? username, Status = UserStatus.Active, Added = now, StatusChanged = now });
            _identities.Insert(new UserIdentity { UserId = user.Id, Provider = UserIdentity.DiscordProvider, ExternalId = discordUserId.ToString(), Username = username, DisplayName = displayName, Linked = now });
            _audit.Record(Actor.Discord(discordUserId, username, user.Id), "user.admin-record", "user", user.Id.ToString());

            return user;
        }

        public ServiceResult SetQuota(ulong discordUserId, ContentType type, int limit, int days, Actor actor)
        {
            if (!ContentTypes.IsOffered(type))
            {
                return ServiceResult.Fail(DiscordText.TypeNotOffered);
            }

            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            days = Math.Clamp(days, 1, 365);
            var row = GetOrCreateAccess(user.Id, type);
            row.QuotaLimit = limit > 0 ? limit : null;
            row.QuotaWindow = limit > 0 ? TimeSpan.FromDays(days) : null;
            _access.Upsert(row);

            _audit.Record(actor, "user.quota", "user", user.Id.ToString(), limit > 0 ? $"{type}: {limit} per {days} days" : $"{type}: no quota");

            return ServiceResult.Ok(DiscordText.QuotaChanged(user.DisplayName, type, row.QuotaLimit, days));
        }

        public ServiceResult Info(ulong discordUserId)
        {
            var identity = _identities.Find(UserIdentity.DiscordProvider, discordUserId.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            if (user == null)
            {
                return ServiceResult.Fail(DiscordText.NotARequestarrUser);
            }

            return ServiceResult.Ok(DiscordText.UserInfo(user, identity, _access.GetForUser(user.Id), _accounts.GetForUser(user.Id)));
        }

        public async Task HandleMemberLeftAsync(ulong discordUserId)
        {
            var user = FindByDiscordId(discordUserId);

            if (user == null || user.Status != UserStatus.Active)
            {
                return;
            }

            _logger.Info("Requestarr user {0} left the Discord server; suspending", user.DisplayName);
            await SuspendAsync(discordUserId, Actor.System, "member left the server");

            // An account event: the admins hear about it in the admin channel (never the feed).
            if (ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel))
            {
                try
                {
                    await _gateway.SendMessageAsync(adminChannel, new InteractionReply { Content = DiscordText.MemberLeftAdminNotice(user.DisplayName) });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not tell the admins that {0} left", user.DisplayName);
                }
            }
        }

        public ServiceResult LinkMediaServer(ulong discordUserId, MediaServerProvider provider, string username, Actor actor)
        {
            if (!ValidUsername(username))
            {
                return ServiceResult.Fail(DiscordText.InvalidUsername);
            }

            var (user, error) = Lookup(discordUserId);

            if (user == null)
            {
                return ServiceResult.Fail(error);
            }

            if (user.PrivateChannelId == null)
            {
                return ServiceResult.Fail(DiscordText.NoChannel);
            }

            Link(user, provider, username.Trim());
            _audit.Record(actor, "mediaserver.link", "user", user.Id.ToString(), $"{provider} {username.Trim()}");
            _panels.RefreshUserPanelAsync(user).GetAwaiter().GetResult();

            return ServiceResult.Ok(DiscordText.MediaServerLinked(user.DisplayName, provider, username.Trim()));
        }

        public async Task<ServiceResult> RequestMediaServerAccessAsync(RequestarrUser user, ulong discordUserId, MediaServerProvider provider, string username)
        {
            if (!ValidUsername(username))
            {
                return ServiceResult.Fail(DiscordText.InvalidUsername);
            }

            if (_accessRequests.GetPendingForUser(user.Id).Any(r => r.Provider == provider))
            {
                return ServiceResult.Fail(DiscordText.AccessRequestAlreadyPending);
            }

            var request = _accessRequests.Insert(new MediaServerAccessRequest
            {
                UserId = user.Id,
                Provider = provider,
                Username = username.Trim(),
                Status = AccessRequestStatus.Pending,
                Requested = DateTime.UtcNow
            });

            var adminChannel = ulong.Parse(_guildSetup.GetConfig().AdminChannelId);
            await _gateway.SendMessageAsync(adminChannel, AccessRequestCard(request, user, discordUserId));
            await _panels.RefreshAdminPanelAsync();

            _audit.Record(new Actor($"discord:{discordUserId}", user.Id), "mediaserver.request", "mediaserver-request", request.Id.ToString(), $"{provider} {request.Username}");

            return ServiceResult.Ok(DiscordText.AccessRequestSent(provider));
        }

        public async Task<ServiceResult> DecideAccessRequestAsync(int requestId, bool approve, Actor admin, string adminName)
        {
            var request = _accessRequests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestNotFound);
            }

            if (request.Status != AccessRequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.AccessRequestAlreadyDecided);
            }

            var user = _users.Get(request.UserId);

            request.Status = approve ? AccessRequestStatus.Approved : AccessRequestStatus.Denied;
            request.Decided = DateTime.UtcNow;
            request.DecidedByUserId = admin.UserId;
            _accessRequests.Update(request);
            await _panels.RefreshAdminPanelAsync();

            if (approve)
            {
                Link(user, request.Provider, request.Username);
                await _panels.RefreshUserPanelAsync(user);
            }

            if (user.Status == UserStatus.Active && ulong.TryParse(user.PrivateChannelId, out var channelId))
            {
                await _gateway.SendMessageAsync(channelId, new InteractionReply
                {
                    Content = approve ? DiscordText.AccessRequestApprovedUser(request.Provider, request.Username) : DiscordText.AccessRequestDeniedUser(request.Provider)
                });
            }

            _audit.Record(admin, approve ? "mediaserver.approve" : "mediaserver.deny", "mediaserver-request", request.Id.ToString(), $"{request.Provider} {request.Username}");

            return ServiceResult.Ok(approve
                ? DiscordText.AccessRequestApproved(user.DisplayName, request.Provider, request.Username, adminName)
                : DiscordText.AccessRequestDenied(user.DisplayName, request.Provider, request.Username, adminName));
        }

        public static InteractionReply AccessRequestCard(MediaServerAccessRequest request, RequestarrUser user, ulong discordUserId)
        {
            return new InteractionReply
            {
                Content = DiscordText.AccessRequestCard(user.DisplayName, discordUserId, request.Provider, request.Username),
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{MediaServerRoutes.Approve}:{request.Id}", Label = DiscordText.ApproveLabel, Style = ButtonStyle.Success },
                    new() { CustomId = $"{MediaServerRoutes.Deny}:{request.Id}", Label = DiscordText.DenyLabel, Style = ButtonStyle.Danger }
                }
            };
        }

        private void Link(RequestarrUser user, MediaServerProvider provider, string username)
        {
            var account = _accounts.Find(user.Id, provider) ?? new MediaServerAccount { UserId = user.Id, Provider = provider, Added = DateTime.UtcNow };
            account.Username = username;
            account.ShareStatus = ShareStatus.None;
            _accounts.Upsert(account);
        }

        private (RequestarrUser User, string Error) Lookup(ulong discordUserId)
        {
            var user = FindByDiscordId(discordUserId);

            if (user == null)
            {
                return (null, DiscordText.NotARequestarrUser);
            }

            if (user.Status == UserStatus.Removed)
            {
                return (null, DiscordText.UserRemoved);
            }

            return (user, null);
        }

        // The shared members role: it shows the announcements channel. Someone who left the server has no
        // roles to take, so failures are only logged.
        private async Task SetMembership(ulong discordUserId, bool member)
        {
            if (_guildSetup.MembersRoleId is not { } role)
            {
                return;
            }

            try
            {
                if (member)
                {
                    await _gateway.AddRoleToMemberAsync(discordUserId, role);
                }
                else
                {
                    await _gateway.RemoveRoleFromMemberAsync(discordUserId, role);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not {0} the members role for {1}", member ? "give" : "take", discordUserId);
            }
        }

        private UserIdentity Identity(RequestarrUser user)
        {
            return _identities.GetForUser(user.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider);
        }

        private async Task SetChannelLock(RequestarrUser user, bool locked, string notice)
        {
            if (!ulong.TryParse(user.PrivateChannelId, out var channelId) || !ulong.TryParse(user.DiscordRoleId, out var roleId) ||
                !await _gateway.ChannelExistsAsync(channelId))
            {
                return;
            }

            await _gateway.SetChannelOverwritesAsync(channelId, ChannelLayout.UserChannel(_gateway.GuildId, _gateway.BotUserId, _guildSetup.AdminRoleId, roleId, locked));
            await _gateway.SendMessageAsync(channelId, new InteractionReply { Content = notice });
        }

        private List<UserMediaAccess> SaveAccess(int userId, IDictionary<ContentType, AccessState> requested)
        {
            var existing = _access.GetForUser(userId);
            var rows = new List<UserMediaAccess>();

            foreach (var type in ContentTypes.All)
            {
                var row = existing.FirstOrDefault(a => a.ContentType == type) ?? new UserMediaAccess { UserId = userId, ContentType = type };

                row.State = ContentTypes.IsOffered(type) && requested != null && requested.TryGetValue(type, out var state)
                    ? state
                    : DefaultAccess[type];

                _access.Upsert(row);
                rows.Add(row);
            }

            return rows;
        }

        private UserMediaAccess GetOrCreateAccess(int userId, ContentType type)
        {
            return _access.GetForUser(userId).FirstOrDefault(a => a.ContentType == type) ??
                   new UserMediaAccess { UserId = userId, ContentType = type, State = DefaultAccess[type] };
        }

        private async Task RollBack(ulong? roleId, ulong? channelId)
        {
            try
            {
                if (channelId.HasValue)
                {
                    await _gateway.DeleteChannelAsync(channelId.Value);
                }

                if (roleId.HasValue)
                {
                    await _gateway.DeleteRoleAsync(roleId.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Rollback after failed user add was incomplete");
            }
        }

        private static bool ValidUsername(string username) => username.IsNotNullOrWhiteSpace() && username.Trim().Length <= 100;
    }

    public static class MediaServerRoutes
    {
        public const string Approve = "msreq-approve";
        public const string Deny = "msreq-deny";
    }
}
