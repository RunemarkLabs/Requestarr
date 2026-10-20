using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Custom IDs of the admin console. {d} is the member's Discord ID; every click is checked again
    // on the server (admin, admin channel, the member's current state).
    public static class AdminRoutes
    {
        public const string Pick = "adm-pick";              // admin panel: Discord's member picker
        public const string Member = "adm-member";          // picker / users list: open a member card
        public const string Add = "adm-add";                // adm-add:{d}
        public const string Access = "adm-access";          // adm-access:{d}, value type:level
        public const string Instance = "adm-instance";      // adm-instance:{d}, value type:backendId
        public const string Limit = "adm-limit";            // adm-limit:{d}, value type -> form
        public const string LimitForm = "adm-limit-form";   // adm-limit-form:{d}:{type}
        public const string Plex = "adm-plex";              // adm-plex:{d} -> form
        public const string PlexForm = "adm-plex-form";     // adm-plex-form:{d}
        public const string Link = "adm-link";              // adm-link:{d}:{accessRequestId}:{a|d}
        public const string Suspend = "adm-suspend";
        public const string Unsuspend = "adm-unsuspend";
        public const string Remove = "adm-remove";          // asks first
        public const string RemoveConfirm = "adm-remove-ok";
        public const string Refresh = "adm-refresh";
        public const string Requests = "adm-requests";      // adm-requests:{d}
        public const string Queue = "adm-queue";            // adm-queue:{type}
        public const string Request = "adm-request";        // menu: open a request card
        public const string LimitField = "limit";
        public const string DaysField = "days";
        public const string PlexField = "username";
    }

    // /admin user {member}, /admin request {id or title}, and everything they open: member cards,
    // request cards, the member picker, the pending queue and the users list.
    public class AdminConsoleHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IUserManagementService _users;
        private readonly IRequestService _requests;
        private readonly IRequestRepository _requestStore;
        private readonly IRequestFeed _feed;
        private readonly IRequestarrUserRepository _userStore;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly IMediaServerAccessRequestRepository _accessRequests;
        private readonly IBackendService _backends;
        private readonly IDiscordGuildGateway _gateway;

        public AdminConsoleHandler(ICallerResolver callers,
                                   IUserManagementService users,
                                   IRequestService requests,
                                   IRequestRepository requestStore,
                                   IRequestFeed feed,
                                   IRequestarrUserRepository userStore,
                                   IUserIdentityRepository identities,
                                   IUserMediaAccessRepository access,
                                   IMediaServerAccountRepository accounts,
                                   IMediaServerAccessRequestRepository accessRequests,
                                   IBackendService backends,
                                   IDiscordGuildGateway gateway)
        {
            _callers = callers;
            _users = users;
            _requests = requests;
            _requestStore = requestStore;
            _feed = feed;
            _userStore = userStore;
            _identities = identities;
            _access = access;
            _accounts = accounts;
            _accessRequests = accessRequests;
            _backends = backends;
            _gateway = gateway;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "admin user"),
            (InteractionKind.SlashCommand, "admin request"),
            (InteractionKind.Autocomplete, "admin request"),
            (InteractionKind.Button, PanelRoutes.AdminQueue),
            (InteractionKind.Button, PanelRoutes.AdminUsers),
            (InteractionKind.Button, AdminRoutes.Pick),
            (InteractionKind.SelectMenu, AdminRoutes.Member),
            (InteractionKind.Button, AdminRoutes.Add),
            (InteractionKind.SelectMenu, AdminRoutes.Access),
            (InteractionKind.SelectMenu, AdminRoutes.Instance),
            (InteractionKind.SelectMenu, AdminRoutes.Limit),
            (InteractionKind.ModalSubmit, AdminRoutes.LimitForm),
            (InteractionKind.Button, AdminRoutes.Plex),
            (InteractionKind.ModalSubmit, AdminRoutes.PlexForm),
            (InteractionKind.Button, AdminRoutes.Link),
            (InteractionKind.Button, AdminRoutes.Suspend),
            (InteractionKind.Button, AdminRoutes.Unsuspend),
            (InteractionKind.Button, AdminRoutes.Remove),
            (InteractionKind.Button, AdminRoutes.RemoveConfirm),
            (InteractionKind.Button, AdminRoutes.Refresh),
            (InteractionKind.Button, AdminRoutes.Requests),
            (InteractionKind.Button, AdminRoutes.Queue),
            (InteractionKind.SelectMenu, AdminRoutes.Request)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminDenial != null)
            {
                if (interaction.Kind == InteractionKind.Autocomplete)
                {
                    await responder.SuggestAsync(Array.Empty<InteractionChoice>());
                    return;
                }

                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            var actor = HandlerSupport.ActorFor(interaction, caller);
            var args = interaction.CustomIdArgs;
            ulong.TryParse(First(args), out var member);

            switch (interaction.Route)
            {
                case "admin user":
                {
                    var target = interaction.GetSnowflake("member");

                    if (target == null)
                    {
                        await responder.Ephemeral(DiscordText.MemberNotFound);
                        return;
                    }

                    await responder.RespondAsync(Ephemeral(await MemberCard(target.Value)));
                    return;
                }

                case "admin request" when interaction.Kind == InteractionKind.Autocomplete:
                    await responder.SuggestAsync(Search(interaction.GetOption<string>("request")).Take(DiscordLimits.ChoicesPerMenu)
                        .Select(r => new InteractionChoice(DiscordText.RequestChoice(r, _requests.OwnerName(r)), r.Id.ToString())).ToList());
                    return;

                case "admin request":
                {
                    var query = interaction.GetOption<string>("request")?.Trim() ?? string.Empty;
                    var matches = Search(query).ToList();

                    if (!matches.Any())
                    {
                        await responder.Ephemeral(DiscordText.NoRequestMatches);
                        return;
                    }

                    await responder.RespondAsync(matches.Count == 1
                        ? Ephemeral(_feed.CardFor(matches[0]))
                        : RequestList(DiscordText.RequestMatches(query, matches.Count), matches));
                    return;
                }

                case PanelRoutes.AdminQueue:
                    await responder.RespondAsync(QueueView(null));
                    return;

                case AdminRoutes.Queue:
                    await responder.UpdateMessageAsync(QueueView(HandlerSupport.ParseType(First(args))));
                    return;

                case PanelRoutes.AdminUsers:
                    await responder.RespondAsync(UsersView());
                    return;

                case AdminRoutes.Pick:
                    await responder.RespondAsync(new InteractionReply
                    {
                        Content = DiscordText.PickMemberPrompt,
                        Ephemeral = true,
                        SelectMenu = new InteractionSelectMenu { Kind = SelectMenuKind.User, CustomId = AdminRoutes.Member, Placeholder = DiscordText.PickMemberPlaceholder }
                    });
                    return;

                case AdminRoutes.Member:
                {
                    if (!ulong.TryParse(First(interaction.Values), out var picked))
                    {
                        await responder.Ephemeral(DiscordText.MemberNotFound);
                        return;
                    }

                    await responder.RespondAsync(Ephemeral(await MemberCard(picked)));
                    return;
                }

                case AdminRoutes.Request:
                {
                    var request = int.TryParse(First(interaction.Values), out var id) ? _requests.Find(id) : null;

                    if (request == null)
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    await responder.RespondAsync(Ephemeral(_feed.CardFor(request)));
                    return;
                }

                case AdminRoutes.Requests:
                {
                    var user = _users.FindByDiscordId(member);
                    var list = user == null ? new List<Request>() : _requests.List(user, false, null, null);
                    await responder.RespondAsync(RequestList(DiscordText.MemberRequestsHeader(user?.DisplayName ?? member.ToString(), list.Count), list));
                    return;
                }
            }

            if (member == 0)
            {
                await responder.Ephemeral(DiscordText.MemberNotFound);
                return;
            }

            switch (interaction.Route)
            {
                case AdminRoutes.Refresh:
                    await responder.UpdateMessageAsync(await MemberCard(member));
                    return;

                case AdminRoutes.Remove:
                    await responder.UpdateMessageAsync(await MemberCard(member, confirmRemove: true));
                    return;

                case AdminRoutes.Limit:
                {
                    var type = HandlerSupport.ParseType(First(interaction.Values));

                    if (type == null)
                    {
                        await responder.Ephemeral(DiscordText.TypeNotOffered);
                        return;
                    }

                    await responder.ShowModalAsync(new InteractionModal
                    {
                        CustomId = $"{AdminRoutes.LimitForm}:{member}:{Key(type.Value)}",
                        Title = $"{DiscordText.LimitTitle}: {DiscordText.Label(type.Value)}",
                        Fields = new List<InteractionTextField>
                        {
                            new(AdminRoutes.LimitField, DiscordText.LimitField, Required: true, MaxLength: 4, Paragraph: false),
                            new(AdminRoutes.DaysField, DiscordText.DaysField, Required: false, MaxLength: 3, Paragraph: false)
                        }
                    });
                    return;
                }

                case AdminRoutes.Plex:
                    await responder.ShowModalAsync(new InteractionModal
                    {
                        CustomId = $"{AdminRoutes.PlexForm}:{member}",
                        Title = DiscordText.PlexTitle,
                        Fields = new List<InteractionTextField> { new(AdminRoutes.PlexField, DiscordText.PlexField, Required: true, MaxLength: 100, Paragraph: false) }
                    });
                    return;

                // Quick changes: the card changes in place, the outcome comes privately.
                case AdminRoutes.Access:
                case AdminRoutes.Instance:
                case AdminRoutes.LimitForm:
                case AdminRoutes.PlexForm:
                {
                    var result = QuickChange(interaction, member, actor);
                    await responder.UpdateMessageAsync(await MemberCard(member));
                    await responder.FollowupAsync(Private(result));
                    return;
                }

                // Changes that call Discord several times: acknowledge first.
                case AdminRoutes.Add:
                case AdminRoutes.Suspend:
                case AdminRoutes.Unsuspend:
                case AdminRoutes.RemoveConfirm:
                case AdminRoutes.Link:
                {
                    await responder.DeferAsync();
                    var result = interaction.Route switch
                    {
                        AdminRoutes.Add => await _users.AddAsync(member, new Dictionary<ContentType, AccessState>(), actor),
                        AdminRoutes.Suspend => await _users.SuspendAsync(member, actor),
                        AdminRoutes.Unsuspend => await _users.UnsuspendAsync(member, actor),
                        AdminRoutes.RemoveConfirm => await _users.RemoveAsync(member, actor),
                        _ => await DecideLink(interaction, member, actor)
                    };

                    await responder.EditAsync(await MemberCard(member));
                    await responder.FollowupAsync(Private(result));
                    return;
                }
            }
        }

        private ServiceResult QuickChange(DiscordInteraction interaction, ulong member, Actor actor)
        {
            var args = interaction.CustomIdArgs;

            switch (interaction.Route)
            {
                case AdminRoutes.Access:
                {
                    var parts = First(interaction.Values)?.Split(':');
                    var type = HandlerSupport.ParseType(parts?.ElementAtOrDefault(0));
                    var level = HandlerSupport.ParseLevel(parts?.ElementAtOrDefault(1));

                    return type == null || level == null ? ServiceResult.Fail(DiscordText.TypeNotOffered) : _users.SetAccess(member, type.Value, level.Value, actor);
                }

                case AdminRoutes.Instance:
                {
                    var parts = First(interaction.Values)?.Split(':');
                    var type = HandlerSupport.ParseType(parts?.ElementAtOrDefault(0));
                    var wanted = parts?.ElementAtOrDefault(1);
                    var instance = int.TryParse(wanted, out var id) ? _backends.Find(id) : _backends.FindByName(wanted ?? string.Empty);

                    if (type == null)
                    {
                        return ServiceResult.Fail(DiscordText.TypeNotOffered);
                    }

                    // The service checks the instance (enabled, right type) and refuses it by name.
                    return _users.SetAccess(member, type.Value, AccessFor(member, type.Value)?.State ?? UserManagementService.DefaultAccess[type.Value], actor, instance?.Name ?? wanted);
                }

                case AdminRoutes.LimitForm:
                {
                    var type = HandlerSupport.ParseType(args.ElementAtOrDefault(1));
                    interaction.Fields.TryGetValue(AdminRoutes.LimitField, out var limitText);
                    interaction.Fields.TryGetValue(AdminRoutes.DaysField, out var daysText);

                    if (type == null || !int.TryParse(limitText?.Trim(), out var limit) || limit < 0 || limit > 1000)
                    {
                        return ServiceResult.Fail(DiscordText.InvalidLimit);
                    }

                    var days = string.IsNullOrWhiteSpace(daysText) ? 7 : int.TryParse(daysText.Trim(), out var d) && d is >= 1 and <= 365 ? d : -1;

                    return days < 0 ? ServiceResult.Fail(DiscordText.InvalidLimit) : _users.SetQuota(member, type.Value, limit, days, actor);
                }

                default:
                {
                    interaction.Fields.TryGetValue(AdminRoutes.PlexField, out var username);
                    return _users.LinkMediaServer(member, MediaServerProvider.Plex, username, actor);
                }
            }
        }

        private async Task<ServiceResult> DecideLink(DiscordInteraction interaction, ulong member, Actor actor)
        {
            var args = interaction.CustomIdArgs;

            if (!int.TryParse(args.ElementAtOrDefault(1), out var requestId))
            {
                return ServiceResult.Fail(DiscordText.AccessRequestNotFound);
            }

            return await _users.DecideAccessRequestAsync(requestId, args.ElementAtOrDefault(2) == "a", actor, interaction.DisplayName ?? interaction.Username);
        }

        // Everything that applies to the member right now, and nothing else.
        public async Task<InteractionReply> MemberCard(ulong member, bool confirmRemove = false)
        {
            var user = _users.FindByDiscordId(member);
            var buttons = new List<InteractionButton>();

            if (user == null || user.Status == UserStatus.Removed || user.PrivateChannelId == null)
            {
                var guildMember = await _gateway.GetMemberAsync(member);
                var name = user?.DisplayName ?? guildMember?.DisplayName ?? guildMember?.Username ?? member.ToString();
                var intro = user?.Status == UserStatus.Removed ? _users.Info(member).Message + "\n" : string.Empty;
                buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Add}:{member}", Label = DiscordText.AddMemberLabel, Style = ButtonStyle.Success });

                return new InteractionReply { Content = intro + DiscordText.MemberNotAdded(name, member), Buttons = buttons };
            }

            var lines = new List<string> { _users.Info(member).Message };
            var access = _access.GetForUser(user.Id);
            var instances = _backends.All().Where(b => b.Enable).ToList();

            foreach (var type in ContentTypes.Offered)
            {
                var row = access.FirstOrDefault(a => a.ContentType == type);
                var assigned = row?.BackendId is { } id ? instances.FirstOrDefault(i => i.Id == id)?.Name : null;

                if (assigned != null)
                {
                    lines.Add(DiscordText.InstanceLine(type, assigned));
                }
            }

            var pending = _accessRequests.GetPendingForUser(user.Id);

            foreach (var link in pending)
            {
                lines.Add(DiscordText.PendingLink(link.Provider, link.Username));
            }

            if (confirmRemove)
            {
                lines.Add(DiscordText.ConfirmRemove(user.DisplayName));

                return new InteractionReply
                {
                    Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                    Buttons = new List<InteractionButton>
                    {
                        new() { CustomId = $"{AdminRoutes.RemoveConfirm}:{member}", Label = DiscordText.ConfirmRemoveLabel, Style = ButtonStyle.Danger },
                        new() { CustomId = $"{AdminRoutes.Refresh}:{member}", Label = DiscordText.KeepLabel, Style = ButtonStyle.Secondary }
                    }
                };
            }

            var accessMenu = new InteractionSelectMenu
            {
                CustomId = $"{AdminRoutes.Access}:{member}",
                Placeholder = DiscordText.ChangeAccessPlaceholder,
                Options = ContentTypes.Offered.SelectMany(type => new[] { AccessState.Off, AccessState.NeedsApproval, AccessState.AutoApprove }.Select(state =>
                {
                    var current = (access.FirstOrDefault(a => a.ContentType == type)?.State ?? UserManagementService.DefaultAccess[type]) == state;
                    return new InteractionSelectOption(DiscordText.AccessOption(type, state), $"{Key(type)}:{LevelKey(state)}", current ? DiscordText.Current : null);
                })).ToList()
            };

            var limitMenu = new InteractionSelectMenu
            {
                CustomId = $"{AdminRoutes.Limit}:{member}",
                Placeholder = DiscordText.SetLimitPlaceholder,
                Options = ContentTypes.Offered.Select(type => new InteractionSelectOption(DiscordText.LimitOption(type, access.FirstOrDefault(a => a.ContentType == type)), Key(type))).ToList()
            };

            var more = new List<InteractionSelectMenu> { limitMenu };
            var choices = ContentTypes.Offered
                .SelectMany(type => instances.Where(i => i.Implementation == BackendImplementations.ForContentType(type)).Select(i => (type, i)))
                .GroupBy(x => x.type).Where(g => g.Count() > 1).SelectMany(g => g)
                .Select(x => new InteractionSelectOption(DiscordText.InstanceOption(x.type, x.i.Name), $"{Key(x.type)}:{x.i.Id}", x.i.Settings.IsDefault ? "default" : null))
                .ToList();

            if (choices.Any())
            {
                more.Add(new InteractionSelectMenu { CustomId = $"{AdminRoutes.Instance}:{member}", Placeholder = DiscordText.ChangeInstancePlaceholder, Options = choices });
            }

            var linked = _accounts.GetForUser(user.Id).Any();
            buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Plex}:{member}", Label = linked ? DiscordText.ChangePlexLabel : DiscordText.AdminLinkPlexLabel, Style = ButtonStyle.Secondary });
            buttons.Add(user.Status == UserStatus.Suspended
                ? new InteractionButton { CustomId = $"{AdminRoutes.Unsuspend}:{member}", Label = DiscordText.UnsuspendLabel, Style = ButtonStyle.Success }
                : new InteractionButton { CustomId = $"{AdminRoutes.Suspend}:{member}", Label = DiscordText.SuspendLabel, Style = ButtonStyle.Secondary });
            buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Remove}:{member}", Label = DiscordText.RemoveLabel, Style = ButtonStyle.Danger });
            buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Requests}:{member}", Label = DiscordText.TheirRequestsLabel, Style = ButtonStyle.Secondary });

            foreach (var link in pending.Take(1))
            {
                buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Link}:{member}:{link.Id}:a", Label = DiscordText.ApproveLinkLabel, Style = ButtonStyle.Success });
                buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Link}:{member}:{link.Id}:d", Label = DiscordText.DenyLinkLabel, Style = ButtonStyle.Danger });
            }

            return new InteractionReply
            {
                Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                SelectMenu = accessMenu,
                MoreMenus = more,
                Buttons = buttons
            };
        }

        // Requests by number ("12", "#12") or by part of the title, open ones first.
        private IEnumerable<Request> Search(string query)
        {
            query = query?.Trim().TrimStart('#') ?? string.Empty;
            var all = _requestStore.All();

            if (int.TryParse(query, out var id))
            {
                return all.Where(r => r.Id == id || r.Id.ToString().StartsWith(query)).OrderBy(r => r.Id == id ? 0 : 1).ThenByDescending(r => r.Id);
            }

            return all.Where(r => query.Length == 0 || r.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.IsActive ? 0 : 1).ThenByDescending(r => r.Id);
        }

        private InteractionReply QueueView(ContentType? type)
        {
            var pending = _requests.List(null, true, RequestStatus.Pending, type);
            var reply = RequestList(pending.Any() ? DiscordText.QueueHeader(type.HasValue ? DiscordText.Label(type.Value) : null, pending.Count) : DiscordText.QueueEmpty, pending);

            // Filters by type (the member card's "Their requests" filters by person).
            reply.Buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Queue}:all", Label = DiscordText.AllTypesLabel, Style = type == null ? ButtonStyle.Primary : ButtonStyle.Secondary });

            foreach (var offered in ContentTypes.Offered)
            {
                reply.Buttons.Add(new InteractionButton { CustomId = $"{AdminRoutes.Queue}:{Key(offered)}", Label = DiscordText.Label(offered), Style = type == offered ? ButtonStyle.Primary : ButtonStyle.Secondary });
            }

            return reply;
        }

        // A list of requests with a menu that opens their cards.
        private InteractionReply RequestList(string header, IReadOnlyList<Request> requests)
        {
            var lines = new List<string> { header };
            lines.AddRange(requests.Take(RequestHandler.MaxListLines).Select(r => DiscordText.RequestLine(r, _requests.OwnerName(r), true)));

            var reply = new InteractionReply
            {
                Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                Ephemeral = true,
                SelectMenu = requests.Any()
                    ? new InteractionSelectMenu
                    {
                        CustomId = AdminRoutes.Request,
                        Placeholder = DiscordText.PickRequestPlaceholder,
                        Options = requests.Take(DiscordLimits.ChoicesPerMenu).Select(r => new InteractionSelectOption(DiscordText.RequestChoice(r, _requests.OwnerName(r)), r.Id.ToString())).ToList()
                    }
                    : null
            };

            return reply;
        }

        private InteractionReply UsersView()
        {
            var users = _userStore.All().Where(u => u.Status != UserStatus.Removed && u.PrivateChannelId != null).OrderBy(u => u.DisplayName).ToList();
            var lines = new List<string> { DiscordText.UsersHeader(users.Count) };
            lines.AddRange(users.Select(u => DiscordText.UserLine(u, _access.GetForUser(u.Id))));
            var ids = users.Select(u => (User: u, Discord: _identities.GetForUser(u.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId)).Where(x => x.Discord != null).ToList();

            return new InteractionReply
            {
                Content = users.Any() ? DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength) : DiscordText.NoUsers,
                Ephemeral = true,
                SelectMenu = ids.Any()
                    ? new InteractionSelectMenu
                    {
                        CustomId = AdminRoutes.Member,
                        Placeholder = DiscordText.PickMemberPlaceholder,
                        Options = ids.Take(DiscordLimits.ChoicesPerMenu).Select(x => new InteractionSelectOption(x.User.DisplayName, x.Discord, DiscordText.Label(x.User.Status))).ToList()
                    }
                    : null,
                Buttons = new List<InteractionButton> { new() { CustomId = AdminRoutes.Pick, Label = DiscordText.PanelMembers, Style = ButtonStyle.Secondary } }
            };
        }

        private UserMediaAccess AccessFor(ulong member, ContentType type)
        {
            var user = _users.FindByDiscordId(member);
            return user == null ? null : _access.GetForUser(user.Id).FirstOrDefault(a => a.ContentType == type);
        }

        private static InteractionReply Ephemeral(InteractionReply card) => new()
        {
            Content = card.Content,
            Embeds = card.Embeds,
            Buttons = card.Buttons,
            SelectMenu = card.SelectMenu,
            MoreMenus = card.MoreMenus,
            Ephemeral = true
        };

        private static string First(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : null;

        private static InteractionReply Private(ServiceResult result) =>
            new() { Content = DiscordLimits.Truncate(result.Message, DiscordLimits.MessageLength), Ephemeral = true };

        private static string Key(ContentType type) => type switch
        {
            ContentType.Tv => "tv",
            ContentType.Music => "music",
            _ => "movie"
        };

        private static string LevelKey(AccessState state) => state switch
        {
            AccessState.Off => "off",
            AccessState.AutoApprove => "auto",
            _ => "approval"
        };
    }
}
