using System;
using System.Collections.Generic;
using System.Linq;
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

        // Discord lets a bot remove its private replies for 15 minutes after the press.
        public static readonly TimeSpan PrivateReplyLifetime = TimeSpan.FromMinutes(15);

        private readonly Dictionary<(InteractionKind, string), IInteractionHandler> _routes;
        private readonly Logger _logger;

        // The last press of each button, per person, that answered privately: pressing it again replaces that copy.
        private readonly Dictionary<(ulong User, string CustomId), (IInteractionResponder Responder, DateTime At)> _privateCopies = new();

        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

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
                await Reply(interaction.Kind == InteractionKind.Autocomplete ? null : responder, UnknownInteractionMessage);

                return DispatchOutcome.Unhandled;
            }

            try
            {
                await handler.HandleAsync(interaction, responder);
                await ReplaceEarlierCopyAsync(interaction, responder);

                return DispatchOutcome.Handled;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "{0} failed handling {1} '{2}' from user {3}", handler.GetType().Name, interaction.Kind, interaction.Route, interaction.UserId);
                await Reply(interaction.Kind == InteractionKind.Autocomplete ? null : responder, FailedInteractionMessage);

                return DispatchOutcome.Failed;
            }
        }

        // A button that answers privately shows one copy per person: the copy from their previous press of the same button
        // goes once the new one is posted, so pressing Help or Read changes again doesn't stack another.
        private async Task ReplaceEarlierCopyAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            if (interaction.Kind != InteractionKind.Button || interaction.CustomId == null || !responder.HasPrivateReplies)
            {
                return;
            }

            var now = Now();
            var key = (interaction.UserId, interaction.CustomId);
            IInteractionResponder earlier = null;

            lock (_privateCopies)
            {
                foreach (var gone in _privateCopies.Where(c => now - c.Value.At >= PrivateReplyLifetime).Select(c => c.Key).ToList())
                {
                    _privateCopies.Remove(gone);
                }

                if (_privateCopies.TryGetValue(key, out var copy))
                {
                    earlier = copy.Responder;
                }

                _privateCopies[key] = (responder, now);
            }

            if (earlier == null)
            {
                return;
            }

            try
            {
                await earlier.DeletePrivateRepliesAsync();
            }
            catch (Exception ex)
            {
                // Already dismissed, or too old for Discord: it goes when the person reloads Discord.
                _logger.Debug(ex, "Could not remove the earlier private reply to '{0}' for user {1}", interaction.CustomId, interaction.UserId);
            }
        }

        private static async Task Reply(IInteractionResponder responder, string message)
        {
            if (responder is null)
            {
                return;
            }

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
