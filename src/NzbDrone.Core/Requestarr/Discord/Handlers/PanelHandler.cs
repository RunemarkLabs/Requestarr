using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Buttons on the pinned user panel (own channel only). The admin panel's buttons are handled by
    // AdminConsoleHandler (and HelpHandler for Help).
    public class PanelHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IRequestarrUserRepository _userStore;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerConnections _mediaServers;

        public PanelHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IRequestarrUserRepository userStore, IUserMediaAccessRepository access, IMediaServerConnections mediaServers)
        {
            _mediaServers = mediaServers;
            _callers = callers;
            _requests = requests;
            _users = users;
            _userStore = userStore;
            _access = access;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, PanelRoutes.MyRequests),
            (InteractionKind.Button, PanelRoutes.MyLimits),
            (InteractionKind.Button, PanelRoutes.LinkPlex),
            (InteractionKind.ModalSubmit, PanelRoutes.LinkPlexForm)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.UserDenial != null)
            {
                await responder.Ephemeral(caller.UserDenial);
                return;
            }

            // Link Plex is on the panel only while a Plex server is connected; an older panel's button does nothing.
            if (interaction.Route is PanelRoutes.LinkPlex or PanelRoutes.LinkPlexForm && !_mediaServers.IsConnected(MediaServerProvider.Plex))
            {
                await responder.Ephemeral(DiscordText.MediaServerNotConnected(MediaServerProvider.Plex));
                return;
            }

            switch (interaction.Route)
            {
                case PanelRoutes.MyRequests:
                {
                    var list = _requests.List(caller.User, false, null, null);
                    await responder.Ephemeral(list.Any()
                        ? string.Join("\n", list.Take(RequestHandler.MaxListLines).Select(r => DiscordText.RequestLine(r, _requests.OwnerName(r), false)))
                        : DiscordText.NoRequests);
                    break;
                }

                case PanelRoutes.MyLimits:
                    await responder.Ephemeral(_requests.QuotaSummary(caller.User));
                    break;

                case PanelRoutes.LinkPlex:
                    await responder.ShowModalAsync(RequestCards.LinkPlex());
                    break;

                case PanelRoutes.LinkPlexForm:
                {
                    interaction.Fields.TryGetValue(PanelRoutes.PlexUsernameField, out var username);
                    await responder.DeferAsync(ephemeral: true);
                    var result = await _users.RequestMediaServerAccessAsync(caller.User, interaction.UserId, MediaServerProvider.Plex, username);
                    await responder.Ephemeral(result.Message);
                    break;
                }
            }
        }
    }
}
