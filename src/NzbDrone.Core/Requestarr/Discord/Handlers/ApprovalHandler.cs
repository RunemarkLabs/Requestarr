using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /queue, /approve, /deny and the buttons on approval cards and admin feed cards (admin only).
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
            (InteractionKind.SlashCommand, "admin queue"),
            (InteractionKind.SlashCommand, "admin approve"),
            (InteractionKind.SlashCommand, "admin deny"),
            (InteractionKind.SlashCommand, "admin cancel"),
            (InteractionKind.SlashCommand, "admin available"),
            (InteractionKind.Button, RequestRoutes.Available),
            (InteractionKind.Button, RequestRoutes.Approve),
            (InteractionKind.Button, RequestRoutes.Deny),
            (InteractionKind.Button, RequestRoutes.AdminCancel),
            (InteractionKind.ModalSubmit, RequestRoutes.DenyReason)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);
            var denial = interaction.Kind == InteractionKind.SlashCommand ? caller.AdminDenial : caller.AdminButtonDenial;

            if (denial != null)
            {
                await responder.Ephemeral(denial);
                return;
            }

            var inFeed = caller.InFeedChannel && interaction.Kind != InteractionKind.SlashCommand;

            var admin = _users.EnsureAdminUser(interaction.UserId, interaction.Username, interaction.DisplayName);
            var actor = HandlerSupport.ActorFor(interaction, caller) with { UserId = admin.Id };
            var adminName = interaction.DisplayName ?? interaction.Username;

            switch (interaction.Kind)
            {
                case InteractionKind.SlashCommand when interaction.CommandName == "admin queue":
                {
                    var filterId = interaction.GetSnowflake("user");
                    var filterUser = filterId == null ? null : _users.FindByDiscordId(filterId.Value);
                    var type = HandlerSupport.ParseType(interaction.GetOption<string>("type"));
                    var pending = _requests.List(filterUser, filterUser == null, RequestStatus.Pending, type);

                    await responder.Public(pending.Any()
                        ? string.Join("\n", pending.Take(RequestHandler.MaxListLines).Select(r => DiscordText.RequestLine(r, _requests.OwnerName(r), true)))
                        : DiscordText.QueueEmpty);
                    break;
                }

                case InteractionKind.SlashCommand when interaction.CommandName == "admin approve":
                    await responder.DeferAsync();
                    await responder.Reply(await _requests.ApproveAsync((int)(interaction.GetInteger("id") ?? 0), actor, adminName));
                    break;

                case InteractionKind.SlashCommand when interaction.CommandName == "admin deny":
                    await responder.DeferAsync();
                    await responder.Reply(await _requests.DenyAsync((int)(interaction.GetInteger("id") ?? 0), interaction.GetOption<string>("reason"), actor, adminName));
                    break;

                case InteractionKind.SlashCommand when interaction.CommandName == "admin cancel":
                    await responder.DeferAsync();
                    await responder.Reply(await _requests.AdminCancelAsync((int)(interaction.GetInteger("id") ?? 0), interaction.GetOption<string>("reason"), actor, adminName));
                    break;

                case InteractionKind.SlashCommand when interaction.CommandName == "admin available":
                    await responder.DeferAsync();
                    await responder.Reply(await _requests.MarkAvailableAsync((int)(interaction.GetInteger("id") ?? 0), actor, adminName));
                    break;

                case InteractionKind.Button when interaction.Route == RequestRoutes.Available:
                {
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    await Done(responder, inFeed, id, () => _requests.MarkAvailableAsync(id, actor, adminName));
                    break;
                }

                case InteractionKind.Button when interaction.Route == RequestRoutes.AdminCancel:
                {
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    await Done(responder, inFeed, id, () => _requests.AdminCancelAsync(id, null, actor, adminName));
                    break;
                }

                case InteractionKind.Button when interaction.Route == RequestRoutes.Approve:
                {
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    await Done(responder, inFeed, id, () => _requests.ApproveAsync(id, actor, adminName));
                    break;
                }

                case InteractionKind.Button when interaction.Route == RequestRoutes.Deny:
                {
                    // Deny asks for a reason first.
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    var request = _requests.Find(id);

                    if (request == null)
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    if (request.Status != RequestStatus.Pending)
                    {
                        await Done(responder, inFeed, id, () => Task.FromResult(Users.ServiceResult.Fail(DiscordText.NotPending(id, request.Status))));
                        return;
                    }

                    await responder.ShowModalAsync(RequestCards.DenyReason(id));
                    break;
                }

                case InteractionKind.ModalSubmit:
                {
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    interaction.Fields.TryGetValue(RequestRoutes.ReasonField, out var reason);
                    await Done(responder, inFeed, id, () => _requests.DenyAsync(id, reason, actor, adminName));
                    break;
                }
            }
        }

        // Approval card: it shows the request's real state (no buttons once decided) and a late click
        // gets a private note. Feed card: a change reposts it at the bottom (the service does that),
        // so the admin gets the outcome privately; a late click reposts the card as it stands.
        private async Task Done(IInteractionResponder responder, bool inFeed, int id, System.Func<Task<Users.ServiceResult>> action)
        {
            if (!inFeed)
            {
                await RefreshCard(responder, id, await action());
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

        private static bool TryId(DiscordInteraction interaction, out int id)
        {
            id = 0;
            return interaction.CustomIdArgs.Count == 1 && int.TryParse(interaction.CustomIdArgs[0], out id);
        }
    }
}
