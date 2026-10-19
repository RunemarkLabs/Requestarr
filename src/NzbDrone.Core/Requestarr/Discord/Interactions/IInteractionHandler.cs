using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestarr.Core.Discord.Interactions
{
    public interface IInteractionHandler
    {
        // (kind, route) pairs this handler answers, e.g. (SlashCommand, "request movie")
        // or (Button, "request-approve").
        IEnumerable<(InteractionKind Kind, string Route)> Routes { get; }

        Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder);
    }
}
