using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Discord.Handlers
{
    // "Announce to users" on a down alert in the admin channel (offered once the outage is long enough).
    public class OutageHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IConnectionMonitor _monitor;

        public OutageHandler(ICallerResolver callers, IConnectionMonitor monitor)
        {
            _callers = callers;
            _monitor = monitor;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.Button, ConnectionRoutes.Announce)
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);

            if (caller.AdminDenial != null)
            {
                await responder.Ephemeral(caller.AdminDenial);
                return;
            }

            if (interaction.CustomIdArgs.Count != 1 || !int.TryParse(interaction.CustomIdArgs[0], out var instanceId))
            {
                await responder.Ephemeral(DiscordText.AnnounceNotDown(null));
                return;
            }

            // The monitor posts the notice and updates the alert (its button goes away); the reply is private.
            await responder.Ephemeral(await _monitor.AnnounceAsync(instanceId, interaction.DisplayName ?? interaction.Username));
        }
    }
}
