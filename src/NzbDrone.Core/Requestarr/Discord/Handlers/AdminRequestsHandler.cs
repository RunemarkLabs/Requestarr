using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Custom IDs of the walk. {ticks}:{key} is the item the card shows; the queue is read again on
    // every click, so the walk never shows something that was handled meanwhile.
    public static class WalkRoutes
    {
        public const string Move = "adm-walk";          // adm-walk:{next|back|start}:{ticks}:{key}
        public const string Do = "adm-walk-do";         // adm-walk-do:{action}:{ticks}:{key}
        public const string Form = "adm-walk-form";     // adm-walk-form:{deny|cancel}:{ticks}:{key}
    }

    // /admin requests and the admin panel's Pending queue: everything waiting on an admin decision,
    // one private card at a time, oldest first, with that item's actions, Skip and Back. Acting moves
    // straight to the next item.
    public class AdminRequestsHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IAdminQueue _queue;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IMediaServerAccessRequestRepository _accessRequests;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IRequestarrUserRepository _userStore;
        private readonly IBackendService _backends;
        private readonly IConnectionMonitor _monitor;
        private readonly IRemovalService _removals;
        private readonly IAccessRequestRepository _welcomeRequests;

        public AdminRequestsHandler(ICallerResolver callers,
                                    IAdminQueue queue,
                                    IRequestService requests,
                                    IUserManagementService users,
                                    IMediaServerAccessRequestRepository accessRequests,
                                    IUserIdentityRepository identities,
                                    IUserMediaAccessRepository access,
                                    IRequestarrUserRepository userStore,
                                    IBackendService backends,
                                    IConnectionMonitor monitor,
                                    IRemovalService removals,
                                    IAccessRequestRepository welcomeRequests)
        {
            _removals = removals;
            _welcomeRequests = welcomeRequests;
            _callers = callers;
            _queue = queue;
            _requests = requests;
            _users = users;
            _accessRequests = accessRequests;
            _identities = identities;
            _access = access;
            _userStore = userStore;
            _backends = backends;
            _monitor = monitor;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "admin requests"),
            (InteractionKind.Button, PanelRoutes.AdminQueue),
            (InteractionKind.Button, WalkRoutes.Move),
            (InteractionKind.Button, WalkRoutes.Do),
            (InteractionKind.ModalSubmit, WalkRoutes.Form)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            var admin = _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName);
            var actor = HandlerSupport.ActorFor(interaction, caller) with { UserId = admin.Id };
            var adminName = interaction.DisplayName ?? interaction.Username;
            var args = interaction.CustomIdArgs;
            var verb = args.ElementAtOrDefault(0);
            var at = (Ticks: long.TryParse(args.ElementAtOrDefault(1), out var ticks) ? ticks : 0, Key: args.ElementAtOrDefault(2) ?? string.Empty);

            switch (interaction.Route)
            {
                case "admin requests":
                case PanelRoutes.AdminQueue:
                    await responder.RespondAsync(Private(await CardAfter(null, null)));
                    return;

                case WalkRoutes.Move:
                    await responder.UpdateMessageAsync(verb switch
                    {
                        "back" => await CardBefore(at),
                        "start" => await CardAfter(null, null),
                        _ => await CardAfter(at, null)
                    });
                    return;

                case WalkRoutes.Do:
                case WalkRoutes.Form:
                    await HandleAction(interaction, responder, verb, at, actor, adminName);
                    return;
            }
        }

        // An action on the card's item, checked again first: someone may have handled it while this card was open.
        private async Task HandleAction(DiscordInteraction interaction, IInteractionResponder responder, string verb, (long Ticks, string Key) at, Actor actor, string adminName)
        {
            if (!Waiting(at.Key))
            {
                await responder.UpdateMessageAsync(await CardAfter(at, DiscordText.AlreadyHandled));
                return;
            }

            // Force removes for good: the card asks first.
            if (interaction.Route == WalkRoutes.Do && verb == "rm-force")
            {
                await responder.UpdateMessageAsync(ForceConfirmation(at));
                return;
            }

            if (interaction.Route == WalkRoutes.Do && verb is "deny" or "cancel")
            {
                await responder.ShowModalAsync(new InteractionModal
                {
                    CustomId = $"{WalkRoutes.Form}:{verb}:{at.Ticks}:{at.Key}",
                    Title = verb == "deny" ? DiscordText.DenyReasonTitle : DiscordText.CancelReasonTitle,
                    Fields = new List<InteractionTextField> { new(RequestRoutes.ReasonField, verb == "deny" ? DiscordText.DenyReasonLabel : DiscordText.CancelReasonLabel, Required: false) }
                });
                return;
            }

            await responder.DeferAsync();
            interaction.Fields.TryGetValue(RequestRoutes.ReasonField, out var reason);
            var result = await ActAsync(verb, at.Key, reason, actor, adminName);
            await responder.EditAsync(await CardAfter(at, result.Message));
        }

        private InteractionReply ForceConfirmation((long Ticks, string Key) at)
        {
            var proposal = _removals.Find(int.Parse(at.Key.AsSpan(1)));

            return new InteractionReply
            {
                Content = DiscordText.RemovalConfirmForce(proposal.Label, proposal.Seasons),
                Buttons = new List<InteractionButton>
                {
                    new() { CustomId = $"{WalkRoutes.Do}:rm-force-yes:{at.Ticks}:{at.Key}", Label = DiscordText.ConfirmDeleteLabel, Style = ButtonStyle.Danger },
                    new() { CustomId = $"{WalkRoutes.Move}:back:{at.Ticks + 1}:{at.Key}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary }
                }
            };
        }

        private async Task<ServiceResult> ActAsync(string action, string key, string reason, Actor actor, string adminName)
        {
            var id = int.Parse(key.AsSpan(1));

            switch (action)
            {
                case "approve":
                    return await _requests.ApproveAsync(id, actor, adminName);
                case "deny":
                    return await _requests.DenyAsync(id, reason, actor, adminName);
                case "cancel":
                    return await _requests.AdminCancelAsync(id, reason, actor, adminName);
                case "available":
                    return await _requests.MarkAvailableAsync(id, actor, adminName);
                case "search":
                    return await _requests.SearchAgainAsync(id, actor);
                case "retry":
                    return await _requests.RetrySendAsync(id, actor);
                case "link-a":
                case "link-d":
                    return await _users.DecideAccessRequestAsync(id, action == "link-a", actor, adminName);
                case "rm-force-yes":
                    return await _removals.ForceAsync(id, actor, adminName, actor.UserId);
                case "rm-drop":
                    return await _removals.DropAsync(id, actor, adminName, actor.UserId);
                case "check":
                {
                    var instance = _backends.Find(id);
                    var back = instance != null && await _monitor.CheckAsync(instance);
                    return back ? ServiceResult.Ok(DiscordText.InstanceBackNow(instance.Name)) : ServiceResult.Fail(DiscordText.StillDown(instance?.Name));
                }

                default:
                    return ServiceResult.Fail(DiscordText.RequestNotFound);
            }
        }

        private bool Waiting(string key) => _queue.Items().Any(i => i.Key == key);

        // The first item after the given one (or the first of all), or the end of the walk.
        private async Task<InteractionReply> CardAfter((long Ticks, string Key)? after, string note)
        {
            var items = _queue.Items();
            var index = after == null ? 0 : FindIndex(items, i => Compare(i, after.Value) > 0);

            if (items.Count == 0)
            {
                return End(DiscordText.WalkDone, note, false);
            }

            return index < 0 ? End(DiscordText.WalkEnd(items.Count), note, true) : await Card(items, index, note);
        }

        private async Task<InteractionReply> CardBefore((long Ticks, string Key) before)
        {
            var items = _queue.Items();

            if (items.Count == 0)
            {
                return End(DiscordText.WalkDone, null, false);
            }

            var index = FindLastIndex(items, i => Compare(i, before) < 0);
            return await Card(items, Math.Max(index, 0), null);
        }

        private static int Compare(QueueItem item, (long Ticks, string Key) at) =>
            item.Ticks != at.Ticks ? item.Ticks.CompareTo(at.Ticks) : string.CompareOrdinal(item.Key, at.Key);

        private static int FindIndex(IReadOnlyList<QueueItem> items, Func<QueueItem, bool> match)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (match(items[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindLastIndex(IReadOnlyList<QueueItem> items, Func<QueueItem, bool> match)
        {
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (match(items[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static InteractionReply End(string message, string note, bool startOver) => new()
        {
            Content = string.Join("\n", new[] { string.IsNullOrWhiteSpace(note) ? null : $"↳ {note}", message }.Where(l => !string.IsNullOrWhiteSpace(l))),
            Buttons = startOver
                ? new List<InteractionButton> { new() { CustomId = $"{WalkRoutes.Move}:start:0:x", Label = DiscordText.StartOverLabel, Style = ButtonStyle.Primary } }
                : new List<InteractionButton>()
        };

        // One item: the full card, what it's about in plain words, and only the actions that apply.
        private async Task<InteractionReply> Card(IReadOnlyList<QueueItem> items, int index, string note)
        {
            var item = items[index];
            var at = $"{item.Ticks}:{item.Key}";
            var lines = new List<string> { DiscordText.WalkHeader(index + 1, items.Count) };

            if (note != null)
            {
                lines.Add($"↳ {note}");
            }

            var reply = new InteractionReply();

            switch (item.Kind)
            {
                case 'r':
                case 't':
                    await DescribeRequest(item, at, lines, reply);
                    break;

                case 'p':
                    DescribePlexLink(item.Id, at, lines, reply);
                    break;

                case 'x':
                    DescribeRemoval(item.Id, at, lines, reply);
                    break;

                // Approve… and Deny are the card's own buttons (AccessRequestHandler): the chooser opens privately.
                case 'a':
                {
                    var request = _welcomeRequests.Find(item.Id);
                    lines.Add(DiscordText.AccessRequestAdminCard(request));
                    reply.Buttons.Add(new InteractionButton { CustomId = $"{AccessRoutes.Approve}:{request.Id}", Label = DiscordText.ApproveAccessLabel, Style = ButtonStyle.Success });
                    reply.Buttons.Add(new InteractionButton { CustomId = $"{AccessRoutes.Deny}:{request.Id}", Label = DiscordText.DenyLabel, Style = ButtonStyle.Danger });
                    break;
                }

                case 'i':
                {
                    var instance = _backends.Find(item.Id);
                    lines.Add(DiscordText.InstanceWaiting(instance));
                    reply.Buttons.Add(ActionButton("check", at, DiscordText.CheckNowLabel, ButtonStyle.Primary));
                    break;
                }
            }

            reply.Buttons.Add(new InteractionButton { CustomId = $"{WalkRoutes.Move}:next:{at}", Label = DiscordText.SkipLabel, Style = ButtonStyle.Secondary });

            if (index > 0)
            {
                reply.Buttons.Add(new InteractionButton { CustomId = $"{WalkRoutes.Move}:back:{at}", Label = DiscordText.BackLabel, Style = ButtonStyle.Secondary });
            }

            return new InteractionReply
            {
                Content = DiscordLimits.Truncate(string.Join("\n", lines), DiscordLimits.MessageLength),
                Embeds = reply.Embeds,
                Buttons = reply.Buttons
            };
        }

        // A request waiting for approval ('r') or in trouble ('t'): its state, seasons and people, its
        // title card, and the actions for its state.
        private async Task DescribeRequest(QueueItem item, string at, List<string> lines, InteractionReply reply)
        {
            var waiting = item.Kind == 'r';
            var request = _requests.Find(item.Id);
            var instance = _backends.Find(request.BackendId ?? 0);
            var title = await _requests.ItemForAsync(request);

            lines.Add(waiting ? DiscordText.WaitingSince(request) : DiscordText.TroubleText(request, instance?.Name ?? DiscordText.Label(request.ContentType)));

            if (request.ContentType == ContentType.Tv)
            {
                lines.Add(DiscordText.SeasonsLine(DiscordText.Seasons(request)));
            }

            lines.AddRange(_requests.PeopleOn(request));

            if (title != null)
            {
                reply.Embeds.Add(RequestCards.Card(request.ContentType, title, instance?.Name, waiting ? RequestCards.PendingColor : RequestCards.RefusedColor));
            }

            if (waiting)
            {
                reply.Buttons.Add(ActionButton("approve", at, DiscordText.ApproveLabel, ButtonStyle.Success));
                reply.Buttons.Add(ActionButton("deny", at, DiscordText.DenyLabel, ButtonStyle.Danger));
            }
            else
            {
                reply.Buttons.Add(request.Status == RequestStatus.Failed
                    ? ActionButton("retry", at, DiscordText.RetrySendLabel, ButtonStyle.Primary)
                    : ActionButton("search", at, DiscordText.SearchAgainLabel, ButtonStyle.Primary));
            }

            reply.Buttons.Add(ActionButton("available", at, DiscordText.AlreadyAvailableLabel, ButtonStyle.Secondary));
            reply.Buttons.Add(ActionButton("cancel", at, DiscordText.CancelLabel, ButtonStyle.Secondary));
        }

        private void DescribePlexLink(int id, string at, List<string> lines, InteractionReply reply)
        {
            var link = _accessRequests.Find(id);
            var discordId = _identities.GetForUser(link.UserId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;

            lines.Add(DiscordText.PlexLinkWaiting(_userStore.Find(link.UserId)?.DisplayName ?? $"user {link.UserId}", discordId, link.Provider, link.Username, link.Requested));
            lines.Add(DiscordText.FormatAccess(_access.GetForUser(link.UserId)));
            reply.Buttons.Add(ActionButton("link-a", at, DiscordText.ApproveLinkLabel, ButtonStyle.Success));
            reply.Buttons.Add(ActionButton("link-d", at, DiscordText.DenyLinkLabel, ButtonStyle.Danger));
        }

        private void DescribeRemoval(int id, string at, List<string> lines, InteractionReply reply)
        {
            var proposal = _removals.Find(id);
            var flaggers = _removals.FlagsOn(proposal.Id).Select(f => _userStore.Find(f.UserId)?.DisplayName ?? $"user {f.UserId}").ToList();
            var requester = proposal.RequesterUserId.HasValue ? _userStore.Find(proposal.RequesterUserId.Value)?.DisplayName : null;

            lines.Add(DiscordText.RemovalWaiting(proposal, requester ?? "the requester", flaggers));
            reply.Embeds.Add(new InteractionEmbed { Title = proposal.Label, ThumbnailUrl = proposal.PosterUrl, Color = RequestCards.PendingColor, Footer = DiscordText.RemovalFeedFooter(proposal) });
            reply.Buttons.Add(ActionButton("rm-force", at, DiscordText.ForceRemovalLabel, ButtonStyle.Danger));
            reply.Buttons.Add(ActionButton("rm-drop", at, DiscordText.DropRemovalLabel, ButtonStyle.Secondary));
        }

        private static InteractionButton ActionButton(string action, string at, string label, ButtonStyle style) =>
            new() { CustomId = $"{WalkRoutes.Do}:{action}:{at}", Label = label, Style = style };

        private static InteractionReply Private(InteractionReply card) => new()
        {
            Content = card.Content,
            Embeds = card.Embeds,
            Buttons = card.Buttons,
            Ephemeral = true
        };
    }
}
