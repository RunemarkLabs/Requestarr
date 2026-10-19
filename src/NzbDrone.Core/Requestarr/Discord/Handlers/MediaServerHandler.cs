using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /admin mediaserver approve|deny and the Approve / Deny buttons on access-request cards.
    // Users ask for access with the Link Plex button on their panel (PanelHandler).
    public class MediaServerHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IUserManagementService _users;

        public MediaServerHandler(ICallerResolver callers, IUserManagementService users)
        {
            _callers = callers;
            _users = users;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "admin mediaserver approve"),
            (InteractionKind.SlashCommand, "admin mediaserver deny"),
            (InteractionKind.Button, MediaServerRoutes.Approve),
            (InteractionKind.Button, MediaServerRoutes.Deny)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (interaction.Kind == InteractionKind.SlashCommand)
            {
                if (caller.AdminDenial != null)
                {
                    await responder.Ephemeral(caller.AdminDenial);
                    return;
                }

                var id = (int)(interaction.GetInteger("id") ?? 0);
                var approveCommand = interaction.CommandName == "admin mediaserver approve";
                await responder.DeferAsync();
                await responder.Reply(await _users.DecideAccessRequestAsync(id, approveCommand, HandlerSupport.ActorFor(interaction, caller), interaction.DisplayName ?? interaction.Username));
                return;
            }

            // Buttons: the custom ID only says which request; authority comes from the clicking member.
            if (caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            if (interaction.CustomIdArgs.Count != 1 || !int.TryParse(interaction.CustomIdArgs[0], out var requestId))
            {
                await responder.Ephemeral(DiscordText.AccessRequestNotFound);
                return;
            }

            var approve = interaction.Route == MediaServerRoutes.Approve;
            var decision = await _users.DecideAccessRequestAsync(requestId, approve, HandlerSupport.ActorFor(interaction, caller), interaction.DisplayName ?? interaction.Username);

            if (!decision.Success)
            {
                await responder.Ephemeral(decision.Message);
                return;
            }

            // Replace the card so the buttons can't be clicked again.
            await responder.UpdateMessageAsync(new InteractionReply { Content = decision.Message });
        }
    }
}
