using System.Threading.Tasks;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    public static class HandlerSupport
    {
        public static ulong? GetSnowflake(this DiscordInteraction interaction, string name)
        {
            if (!interaction.Options.TryGetValue(name, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                ulong u => u,
                long l when l > 0 => (ulong)l,
                string s when ulong.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        public static long? GetInteger(this DiscordInteraction interaction, string name)
        {
            if (!interaction.Options.TryGetValue(name, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                long l => l,
                int i => i,
                double d => (long)d,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        public static AccessState? ParseLevel(string value) => value?.ToLowerInvariant() switch
        {
            "off" => AccessState.Off,
            "approval" => AccessState.NeedsApproval,
            "auto" => AccessState.AutoApprove,
            _ => null
        };

        public static ContentType? ParseType(string value) => value?.ToLowerInvariant() switch
        {
            "movie" => ContentType.Movie,
            "tv" => ContentType.Tv,
            "music" => ContentType.Music,
            _ => null
        };

        public static Actor ActorFor(DiscordInteraction interaction, CallerContext caller) =>
            Actor.Discord(interaction.UserId, interaction.Username, caller.User?.Id);

        public static Task Ephemeral(this IInteractionResponder responder, string message) => Send(responder, message, true);

        public static Task Public(this IInteractionResponder responder, string message) => Send(responder, message, false);

        public static Task Reply(this IInteractionResponder responder, ServiceResult result) => Send(responder, result.Message, !result.Success);

        private static Task Send(IInteractionResponder responder, string message, bool ephemeral)
        {
            var reply = new InteractionReply { Content = DiscordLimits.Truncate(message, DiscordLimits.MessageLength), Ephemeral = ephemeral };

            return responder.HasResponded ? responder.FollowupAsync(reply) : responder.RespondAsync(reply);
        }
    }
}
