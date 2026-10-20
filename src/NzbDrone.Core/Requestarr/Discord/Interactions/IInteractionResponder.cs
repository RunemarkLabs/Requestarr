using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestarr.Core.Discord.Interactions
{
    // How a handler answers an interaction. Implemented over Discord.Net by the gateway
    // adapter, and by a recording fake in tests.
    public interface IInteractionResponder
    {
        bool HasResponded { get; }

        // Acknowledge now and answer later (Discord requires a response within 3 seconds).
        Task DeferAsync(bool ephemeral = false);

        Task RespondAsync(InteractionReply reply);

        Task FollowupAsync(InteractionReply reply);

        // Components only: replace the message the component belongs to.
        Task UpdateMessageAsync(InteractionReply reply);

        // After DeferAsync: replace the deferred response (commands) or the message the component
        // belongs to (components), so one request stays one message that changes in place.
        Task EditAsync(InteractionReply reply);

        // The message this interaction answers in: the component's message, or the command's
        // own response once it exists. Null when Discord can't say.
        Task<ulong?> MessageIdAsync();

        // Must be the first response: opens a form (for example a deny reason).
        Task ShowModalAsync(InteractionModal modal);

        // Autocomplete only: up to 25 suggestions for the focused option.
        Task SuggestAsync(IReadOnlyList<InteractionChoice> choices);
    }
}
