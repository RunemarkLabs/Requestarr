using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // The buttons on approval cards, admin feed cards and request cards (/admin request), and the
    // deny / cancel forms they open. Admin only, in the admin channel or the feed.
    public class ApprovalHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IRequestFeed _feed;

        public ApprovalHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IRequestFeed feed)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
            _feed = feed;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, RequestRoutes.Available),
            (InteractionKind.Button, RequestRoutes.Approve),
            (InteractionKind.Button, RequestRoutes.Deny),
            (InteractionKind.Button, RequestRoutes.AdminCancel),
            (InteractionKind.ModalSubmit, RequestRoutes.DenyReason),
            (InteractionKind.ModalSubmit, RequestRoutes.CancelReason)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminButtonDenial != null)
            {
                await responder.Ephemeral(caller.AdminButtonDenial);
                return;
            }

            if (!TryId(interaction, out var requestId))
            {
                await responder.Ephemeral(DiscordText.RequestNotFound);
                return;
            }

            // Which card was clicked: the admin feed, the approval card, or a request card from
            // /admin request (or the queue). Each is refreshed its own way.
            var current = _requests.Find(requestId);
            var mode = caller.InFeedChannel ? CardMode.Feed
                : current != null && interaction.MessageId?.ToString() == current.AdminMessageId ? CardMode.Approval
                : CardMode.Request;

            var admin = _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName);
            var actor = HandlerSupport.ActorFor(interaction, caller) with { UserId = admin.Id };
            var adminName = interaction.DisplayName ?? interaction.Username;

            var id = requestId;

            switch (interaction.Route)
            {
                case RequestRoutes.Available:
                    await Done(responder, mode, id, () => _requests.MarkAvailableAsync(id, actor, adminName));
                    break;

                case RequestRoutes.Approve:
                    await Done(responder, mode, id, () => _requests.ApproveAsync(id, actor, adminName));
                    break;

                // Deny and Cancel ask for a reason first (optional for Cancel), unless it's too late.
                case RequestRoutes.Deny when current?.Status != RequestStatus.Pending:
                    await Done(responder, mode, id, () => Task.FromResult(current == null ? Users.ServiceResult.Fail(DiscordText.RequestNotFound) : Users.ServiceResult.Fail(DiscordText.NotPending(id, current.Status))));
                    break;

                case RequestRoutes.Deny:
                    await responder.ShowModalAsync(RequestCards.DenyReason(id));
                    break;

                case RequestRoutes.AdminCancel when current == null || !RequestService.AdminCanCancel(current):
                    await Done(responder, mode, id, () => _requests.AdminCancelAsync(id, null, actor, adminName));
                    break;

                case RequestRoutes.AdminCancel:
                    await responder.ShowModalAsync(RequestCards.CancelReason(id));
                    break;

                case RequestRoutes.CancelReason:
                {
                    interaction.Fields.TryGetValue(RequestRoutes.ReasonField, out var reason);
                    await Done(responder, mode, id, () => _requests.AdminCancelAsync(id, reason, actor, adminName));
                    break;
                }

                case RequestRoutes.DenyReason:
                {
                    interaction.Fields.TryGetValue(RequestRoutes.ReasonField, out var reason);
                    await Done(responder, mode, id, () => _requests.DenyAsync(id, reason, actor, adminName));
                    break;
                }
            }
        }

        // Approval card: it shows the request's real state (no buttons once decided) and a late click
        // gets a private note. Feed card: a change reposts it at the bottom (the service does that),
        // so the admin gets the outcome privately; a late click reposts the card as it stands.
        // Request card (/admin request): it changes in place to the request's card as it now stands,
        // and the outcome comes privately.
        private async Task Done(IInteractionResponder responder, CardMode mode, int id, System.Func<Task<Users.ServiceResult>> action)
        {
            if (mode == CardMode.Approval)
            {
                await RefreshCard(responder, id, await action());
                return;
            }

            if (mode == CardMode.Request)
            {
                await responder.DeferAsync();
                var outcome = await action();
                var now = _requests.Find(id);

                if (now != null)
                {
                    await responder.EditAsync(_feed.CardFor(now));
                }

                await responder.FollowupAsync(new InteractionReply { Content = DiscordLimits.Truncate(outcome.Message, DiscordLimits.MessageLength), Ephemeral = true });
                return;
            }

            await responder.DeferAsync(ephemeral: true);
            var result = await action();
            var request = _requests.Find(id);

            await responder.FollowupAsync(new InteractionReply { Content = DiscordLimits.Truncate(result.Message, DiscordLimits.MessageLength), Ephemeral = true });

            if (!result.Success && request != null)
            {
                await _feed.PostAsync(request);
            }
        }

        // The card shows the request's real state, without buttons once decided; a late click
        // also gets a private note saying why nothing happened.
        private async Task RefreshCard(IInteractionResponder responder, int id, Users.ServiceResult result)
        {
            var request = _requests.Find(id);

            if (request == null)
            {
                await responder.Ephemeral(result.Message);
                return;
            }

            var decidedBy = _requests.DisplayName(request.DecidedByUserId);
            await responder.UpdateMessageAsync(new InteractionReply { Content = DiscordText.DecidedCard(request, _requests.OwnerName(request), decidedBy), KeepEmbeds = true });

            if (!result.Success)
            {
                await responder.Ephemeral(result.Message);
            }
        }

        private enum CardMode
        {
            Approval,
            Feed,
            Request
        }

        private static bool TryId(DiscordInteraction interaction, out int id)
        {
            id = 0;
            return interaction.CustomIdArgs.Count == 1 && int.TryParse(interaction.CustomIdArgs[0], out id);
        }
    }
}
