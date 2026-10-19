using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Discord.Harness
{
    public record InteractionRun(DispatchOutcome Outcome, RecordingResponder Responder)
    {
        public InteractionReply LastReply => Responder.LastReply;
    }

    // Feeds simulated slash commands and component clicks through the real dispatcher into
    // the handlers under test, and records the replies. Later phases pass real handlers built
    // with Mocker (service layer mocked or backed by the test database).
    public class InteractionHarness
    {
        private readonly InteractionDispatcher _dispatcher;

        public InteractionHarness(params IInteractionHandler[] handlers)
            : this((IEnumerable<IInteractionHandler>)handlers)
        {
        }

        public InteractionHarness(IEnumerable<IInteractionHandler> handlers)
        {
            _dispatcher = new InteractionDispatcher(handlers, LogManager.GetLogger(nameof(InteractionHarness)));
        }

        public async Task<InteractionRun> SendAsync(DiscordInteraction interaction)
        {
            var responder = new RecordingResponder();
            var outcome = await _dispatcher.DispatchAsync(interaction, responder);

            return new InteractionRun(outcome, responder);
        }

        public Task<InteractionRun> SendAsync(FakeInteraction interaction) => SendAsync(interaction.Build());
    }
}
