using System;
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
            else if (_interaction is SocketModal modal)
            {
                await modal.UpdateAsync(m =>
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

        public Task ShowModalAsync(InteractionModal modal)
        {
            var builder = new ModalBuilder().WithTitle(DiscordLimits.Truncate(modal.Title, DiscordLimits.ModalTitleLength)).WithCustomId(modal.CustomId);

            foreach (var field in modal.Fields)
            {
                var style = field.Paragraph ? TextInputStyle.Paragraph : TextInputStyle.Short;
                builder.AddTextInput(DiscordLimits.Truncate(field.Label, 45), field.CustomId, style, required: field.Required, maxLength: field.MaxLength);
            }

            return _interaction.RespondWithModalAsync(builder.Build());
        }

        public static MessageComponent Components(InteractionReply reply)
        {
            var hasButtons = reply.Buttons != null && reply.Buttons.Any();

            if (!hasButtons && reply.SelectMenu == null)
            {
                return null;
            }

            var builder = new ComponentBuilder();

            if (reply.SelectMenu != null)
            {
                var menu = new SelectMenuBuilder()
                    .WithCustomId(reply.SelectMenu.CustomId)
                    .WithPlaceholder(DiscordLimits.Truncate(reply.SelectMenu.Placeholder, 150))
                    .WithMinValues(reply.SelectMenu.MinValues)
                    .WithMaxValues(Math.Min(reply.SelectMenu.MaxValues, reply.SelectMenu.Options.Count));

                foreach (var option in reply.SelectMenu.Options.Take(DiscordLimits.ChoicesPerMenu))
                {
                    var label = DiscordLimits.Truncate(option.Label, DiscordLimits.OptionLabelLength);
                    menu.AddOption(label, option.Value, DiscordLimits.Truncate(option.Description, DiscordLimits.OptionLabelLength));
                }

                builder.WithSelectMenu(menu);
            }

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
