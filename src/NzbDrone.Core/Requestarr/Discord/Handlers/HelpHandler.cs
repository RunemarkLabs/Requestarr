using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Metadata;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // User help (/help, the user panel's Help) and admin help (/admin help, the admin panel's Help),
    // kept apart: user help never shows admin topics, even to an admin. Visible only to the asker.
    public class HelpHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly IMetadataService _metadata;
        private readonly Backends.IBackendService _backends;
        private readonly IMediaServerConnections _mediaServers;

        public HelpHandler(ICallerResolver callers, IUserMediaAccessRepository access, IMediaServerAccountRepository accounts, IMetadataService metadata, Backends.IBackendService backends, IMediaServerConnections mediaServers)
        {
            _mediaServers = mediaServers;
            _callers = callers;
            _access = access;
            _accounts = accounts;
            _metadata = metadata;
            _backends = backends;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "help"),
            (InteractionKind.SlashCommand, "admin help"),
            (InteractionKind.Button, PanelRoutes.Help),
            (InteractionKind.Button, PanelRoutes.AdminHelp),
            (InteractionKind.SelectMenu, HelpCatalog.RouteTopic),
            (InteractionKind.Button, HelpCatalog.RouteHome)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);
            var entry = (interaction.Kind, interaction.Route) switch
            {
                (InteractionKind.SlashCommand, "help") or (InteractionKind.Button, PanelRoutes.Help) => HelpTree.User,
                (InteractionKind.SlashCommand, "admin help") or (InteractionKind.Button, PanelRoutes.AdminHelp) => HelpTree.Admin,
                _ => (HelpTree?)null
            };

            var args = interaction.CustomIdArgs;
            var picked = interaction.Values.Count > 0 ? interaction.Values[0] : null;
            var tree = entry ?? HelpCatalog.ParseKey(args.Count > 0 ? args[0] : null) ?? HelpTree.User;

            if (tree != HelpTree.User && caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            var viewer = (tree == HelpTree.User ? Viewer(caller) : HelpViewer.Everything)?.WithTmdb(_metadata.CastEnabled);

            if (viewer == null)
            {
                await responder.Ephemeral(caller.RightServer ? DiscordText.NotAUser : DiscordText.WrongServer);
                return;
            }

            if (entry != null)
            {
                await responder.RespondAsync(HelpCatalog.Home(tree, viewer));
                return;
            }

            var page = interaction.Route == HelpCatalog.RouteHome ? HelpCatalog.Home(tree, viewer)
                : tree == HelpTree.Admin && picked == HelpCatalog.UsersBranchKey ? HelpCatalog.Home(HelpTree.AdminUser, HelpViewer.Everything.WithTmdb(_metadata.CastEnabled))
                : HelpCatalog.Topic(tree, viewer, picked);

            await responder.UpdateMessageAsync(page);
        }

        // An active user sees topics for their own access; an admin who isn't a user sees everything.
        private HelpViewer Viewer(CallerContext caller)
        {
            if (!caller.RightServer)
            {
                return null;
            }

            var user = caller.User;

            if (user != null && user.Status == UserStatus.Active && user.PrivateChannelId != null)
            {
                var access = _access.GetForUser(user.Id);
                var types = ContentTypes.Offered.Where(t => _backends.EverConnected(t) && (access.FirstOrDefault(a => a.ContentType == t)?.State ?? UserManagementService.DefaultAccess[t]) != AccessState.Off).ToList();

                return new HelpViewer(types, _accounts.GetForUser(user.Id).Any(), PlexServer: _mediaServers.IsConnected(MediaServers.MediaServerProvider.Plex));
            }

            return caller.IsAdmin ? HelpViewer.Everything : null;
        }
    }
}
