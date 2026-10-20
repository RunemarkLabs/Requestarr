using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /queue, /approve, /deny and the Approve / Deny buttons on approval cards (admin only).
    public class ApprovalHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;

        public ApprovalHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users)
        {
            _callers = callers;
            _requests = requests;
            _users = users;
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
            (InteractionKind.ModalSubmit, RequestRoutes.DenyReason)
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

                    await RefreshCard(responder, id, await _requests.MarkAvailableAsync(id, actor, adminName));
                    break;
                }

                case InteractionKind.Button when interaction.Route == RequestRoutes.Approve:
                {
                    if (!TryId(interaction, out var id))
                    {
                        await responder.Ephemeral(DiscordText.RequestNotFound);
                        return;
                    }

                    var result = await _requests.ApproveAsync(id, actor, adminName);
                    await RefreshCard(responder, id, result);
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
                        await RefreshCard(responder, id, Users.ServiceResult.Fail(DiscordText.NotPending(id, request.Status)));
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
                    var result = await _requests.DenyAsync(id, reason, actor, adminName);
                    await RefreshCard(responder, id, result);
                    break;
                }
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
