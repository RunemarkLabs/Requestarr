using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Discord.Handlers
{
    // Read changes on a version card: the full card, only to whoever pressed it. Anyone who can see the channel may
    // press it (the admin channel is admins only; the announcements channel is members and admins).
    public class VersionCardHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IAnnouncementService _announcements;

        public VersionCardHandler(ICallerResolver callers, IAnnouncementService announcements)
        {
            _callers = callers;
            _announcements = announcements;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, VersionCardRoutes.ReadChanges)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            if (!_callers.Resolve(interaction).RightServer)
            {
                await responder.Ephemeral(DiscordText.WrongServer);
                return;
            }

            var args = interaction.CustomIdArgs;
            var audience = args.ElementAtOrDefault(0) == "a" ? VersionAudience.Admins : VersionAudience.Users;

            if (!Version.TryParse(args.ElementAtOrDefault(1), out var version))
            {
                await responder.Ephemeral(DiscordText.VersionNotFound(new Version(0, 0, 0)));
                return;
            }

            var cards = _announcements.FullCard(audience, version);
            await responder.RespondAsync(cards[0]);

            foreach (var more in cards.Skip(1))
            {
                await responder.FollowupAsync(more);
            }
        }
    }
}
