using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /mediaserver request (user, own channel) and the admin's Approve / Deny buttons.
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
            (InteractionKind.SlashCommand, "mediaserver request"),
            (InteractionKind.Button, MediaServerRoutes.Approve),
            (InteractionKind.Button, MediaServerRoutes.Deny)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (interaction.Kind == InteractionKind.SlashCommand)
            {
                if (caller.UserDenial != null)
                {
                    await responder.Ephemeral(caller.UserDenial);
                    return;
                }

                await responder.DeferAsync(ephemeral: true);
                var result = await _users.RequestMediaServerAccessAsync(caller.User, interaction.UserId, MediaServerProvider.Plex, interaction.GetOption<string>("username"));
                await responder.Ephemeral(result.Message);
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
