using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Requestarr.Core.Discord.Interactions;
using ButtonStyle = Requestarr.Core.Discord.Interactions.ButtonStyle;

namespace Requestarr.Core.Discord.Gateway
{
    public class DiscordNetResponder : IInteractionResponder
    {
        private readonly SocketInteraction _interaction;

        public DiscordNetResponder(SocketInteraction interaction)
        {
            _interaction = interaction;
        }

        public bool HasResponded => _interaction.HasResponded;

        public Task DeferAsync(bool ephemeral = false) => _interaction.DeferAsync(ephemeral);

        public Task RespondAsync(InteractionReply reply) =>
            _interaction.RespondAsync(reply.Content, embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None);

        public Task FollowupAsync(InteractionReply reply) =>
            _interaction.FollowupAsync(reply.Content, embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None);

        public async Task UpdateMessageAsync(InteractionReply reply)
        {
            if (_interaction is SocketMessageComponent component)
            {
                await component.UpdateAsync(m =>
                {
                    m.Content = reply.Content;
                    m.Components = Components(reply) ?? new ComponentBuilder().Build();
                    m.Embeds = Embeds(reply);
                });
            }
            else
            {
                await RespondAsync(reply);
            }
        }

        public static MessageComponent Components(InteractionReply reply)
        {
            if (reply.Buttons == null || !reply.Buttons.Any())
            {
                return null;
            }

            var builder = new ComponentBuilder();

            foreach (var button in reply.Buttons.Take(25))
            {
                builder.WithButton(DiscordLimits.Truncate(button.Label, 80), button.CustomId, Map(button.Style), disabled: button.Disabled);
            }

            return builder.Build();
        }

        public static Embed[] Embeds(InteractionReply reply)
        {
            if (reply.Embeds == null || !reply.Embeds.Any())
            {
                return null;
            }

            return reply.Embeds.Take(10).Select(e =>
            {
                var builder = new EmbedBuilder().WithTitle(DiscordLimits.Truncate(e.Title, 256)).WithDescription(DiscordLimits.Truncate(e.Description, 4096));

                if (e.Url != null)
                {
                    builder.WithUrl(e.Url);
                }

                if (e.ThumbnailUrl != null)
                {
                    builder.WithThumbnailUrl(e.ThumbnailUrl);
                }

                foreach (var field in e.Fields.Take(25))
                {
                    builder.AddField(DiscordLimits.Truncate(field.Key, 256), DiscordLimits.Truncate(field.Value, 1024), inline: true);
                }

                return builder.Build();
            }).ToArray();
        }

        private static global::Discord.ButtonStyle Map(ButtonStyle style) => style switch
        {
            ButtonStyle.Primary => global::Discord.ButtonStyle.Primary,
            ButtonStyle.Success => global::Discord.ButtonStyle.Success,
            ButtonStyle.Danger => global::Discord.ButtonStyle.Danger,
            _ => global::Discord.ButtonStyle.Secondary
        };
    }
}
