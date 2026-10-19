using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // Buttons on the pinned user panel (own channel only) and the admin panel (admin channel only).
    public class PanelHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IRequestService _requests;
        private readonly IUserManagementService _users;
        private readonly IRequestarrUserRepository _userStore;
        private readonly IUserMediaAccessRepository _access;

        public PanelHandler(ICallerResolver callers, IRequestService requests, IUserManagementService users, IRequestarrUserRepository userStore, IUserMediaAccessRepository access)
        {
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
            (InteractionKind.ModalSubmit, PanelRoutes.LinkPlexForm),
            (InteractionKind.Button, PanelRoutes.AdminQueue),
            (InteractionKind.Button, PanelRoutes.AdminUsers)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (interaction.Route == PanelRoutes.AdminQueue || interaction.Route == PanelRoutes.AdminUsers)
            {
                if (caller.AdminDenial != null)
                {
                    await responder.Ephemeral(caller.AdminDenial);
                    return;
                }

                await responder.Ephemeral(interaction.Route == PanelRoutes.AdminQueue ? Queue() : UserList());
                return;
            }

            if (caller.UserDenial != null)
            {
                await responder.Ephemeral(caller.UserDenial);
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

        private string Queue()
        {
            var pending = _requests.List(null, true, RequestStatus.Pending, null);

            return pending.Any()
                ? string.Join("\n", pending.Take(RequestHandler.MaxListLines).Select(r => DiscordText.RequestLine(r, _requests.OwnerName(r), true)))
                : DiscordText.QueueEmpty;
        }

        private string UserList()
        {
            var users = _userStore.All().Where(u => u.Status != UserStatus.Removed && u.PrivateChannelId != null).OrderBy(u => u.DisplayName).ToList();

            return users.Any()
                ? DiscordLimits.Truncate(string.Join("\n", users.Select(u => DiscordText.UserLine(u, _access.GetForUser(u.Id)))), DiscordLimits.MessageLength)
                : DiscordText.NoUsers;
        }
    }
}
