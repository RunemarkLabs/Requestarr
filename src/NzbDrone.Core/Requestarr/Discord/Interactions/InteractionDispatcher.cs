using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;

namespace Requestarr.Core.Discord.Interactions
{
    public enum DispatchOutcome
    {
        Handled,
        Unhandled,
        Failed
    }

    public interface IInteractionDispatcher
    {
        Task<DispatchOutcome> DispatchAsync(DiscordInteraction interaction, IInteractionResponder responder);
    }

    // Single entry point for every interaction, from the gateway or from tests.
    public class InteractionDispatcher : IInteractionDispatcher
    {
        public const string UnknownInteractionMessage = "That command isn't available.";
        public const string FailedInteractionMessage = "Something went wrong handling that. The admin can check the Requestarr logs.";

        private readonly Dictionary<(InteractionKind, string), IInteractionHandler> _routes;
        private readonly Logger _logger;

        public InteractionDispatcher(IEnumerable<IInteractionHandler> handlers, Logger logger)
        {
            _logger = logger;
            _routes = new Dictionary<(InteractionKind, string), IInteractionHandler>();

            foreach (var handler in handlers)
            {
                foreach (var (kind, route) in handler.Routes)
                {
                    var key = (kind, Normalize(route));

                    if (_routes.TryGetValue(key, out var existing))
                    {
                        throw new InvalidOperationException($"{kind} route '{route}' is claimed by both {existing.GetType().Name} and {handler.GetType().Name}");
                    }

                    _routes[key] = handler;
                }
            }
        }

        public async Task<DispatchOutcome> DispatchAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            if (interaction.Route == null || !_routes.TryGetValue((interaction.Kind, Normalize(interaction.Route)), out var handler))
            {
                _logger.Debug("No handler for {0} '{1}'", interaction.Kind, interaction.Route);
                await Reply(responder, UnknownInteractionMessage);

                return DispatchOutcome.Unhandled;
            }

            try
            {
                await handler.HandleAsync(interaction, responder);

                return DispatchOutcome.Handled;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{0} failed handling {1} '{2}' from user {3}", handler.GetType().Name, interaction.Kind, interaction.Route, interaction.UserId);
                await Reply(responder, FailedInteractionMessage);

                return DispatchOutcome.Failed;
            }
        }

        private static async Task Reply(IInteractionResponder responder, string message)
        {
            var reply = new InteractionReply { Content = message, Ephemeral = true };

            if (responder.HasResponded)
            {
                await responder.FollowupAsync(reply);
            }
            else
            {
                await responder.RespondAsync(reply);
            }
        }

        private static string Normalize(string route)
        {
            return string.Join(' ', route.Trim().TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }
    }
}
