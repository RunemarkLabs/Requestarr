using System;
using System.Collections.Generic;
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

        public async Task RespondAsync(InteractionReply reply)
        {
            var files = Files(reply);

            try
            {
                if (files.Count == 0)
                {
                    await _interaction.RespondAsync(Text(reply), embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None, flags: Flags(reply));
                }
                else
                {
                    await _interaction.RespondWithFilesAsync(files, reply.Content, embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None);
                }
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }
        }

        public async Task FollowupAsync(InteractionReply reply)
        {
            var files = Files(reply);

            try
            {
                if (files.Count == 0)
                {
                    await _interaction.FollowupAsync(Text(reply), embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None, flags: Flags(reply));
                }
                else
                {
                    await _interaction.FollowupWithFilesAsync(files, reply.Content, embeds: Embeds(reply), ephemeral: reply.Ephemeral, components: Components(reply), allowedMentions: AllowedMentions.None);
                }
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }
        }

        // The images the reply's embeds point to (attachment://...), sent with it (DiscordAssets).
        public static List<FileAttachment> Files(InteractionReply reply) =>
            DiscordAssets.For(reply).Select(f => new FileAttachment(new System.IO.MemoryStream(f.Bytes), f.Name)).ToList();

        public async Task UpdateMessageAsync(InteractionReply reply)
        {
            if (_interaction is not SocketMessageComponent && _interaction is not SocketModal)
            {
                await RespondAsync(reply);
                return;
            }

            var files = reply.KeepEmbeds ? new List<FileAttachment>() : Files(reply);

            try
            {
                if (_interaction is SocketMessageComponent component)
                {
                    await component.UpdateAsync(m =>
                    {
                        m.Content = reply.Content;
                        m.Components = Components(reply) ?? new ComponentBuilder().Build();

                        if (!reply.KeepEmbeds)
                        {
                            m.Embeds = Embeds(reply) ?? Array.Empty<Embed>();
                            m.Attachments = files;
                        }
                    });
                }
                else
                {
                    await ((SocketModal)_interaction).UpdateAsync(m =>
                    {
                        m.Content = reply.Content;
                        m.Components = Components(reply) ?? new ComponentBuilder().Build();
                        m.Embeds = Embeds(reply);
                        m.Attachments = files;
                    });
                }
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }
        }

        public async Task EditAsync(InteractionReply reply)
        {
            var files = reply.KeepEmbeds ? new List<FileAttachment>() : Files(reply);

            try
            {
                await _interaction.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = DiscordLimits.Truncate(reply.Content ?? string.Empty, DiscordLimits.MessageLength);

                    if (!reply.KeepEmbeds)
                    {
                        m.Embeds = Embeds(reply) ?? Array.Empty<Embed>();
                        m.Attachments = files;
                    }

                    m.Components = Components(reply) ?? new ComponentBuilder().Build();
                    m.AllowedMentions = AllowedMentions.None;
                });
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }
        }

        public async Task<ulong?> MessageIdAsync()
        {
            if (_interaction is SocketMessageComponent component)
            {
                return component.Message?.Id;
            }

            try
            {
                return (await _interaction.GetOriginalResponseAsync())?.Id;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Task SuggestAsync(IReadOnlyList<InteractionChoice> choices)
        {
            var results = choices.Take(DiscordLimits.ChoicesPerMenu).Select(c => new AutocompleteResult(DiscordLimits.Truncate(c.Name, DiscordLimits.OptionLabelLength), c.Value));

            return ((SocketAutocompleteInteraction)_interaction).RespondAsync(results);
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

        // Each menu takes a row of its own (Discord's limit is 5 rows); buttons follow, 5 to a row.
        public static MessageComponent Components(InteractionReply reply)
        {
            if (reply.IsCards)
            {
                return CardComponents(reply);
            }

            var menus = new[] { reply.SelectMenu }.Concat(reply.MoreMenus ?? new List<InteractionSelectMenu>()).Where(m => m != null).ToList();
            var buttons = reply.Buttons ?? new List<InteractionButton>();

            if (!buttons.Any() && !menus.Any())
            {
                return null;
            }

            var builder = new ComponentBuilder();
            var row = 0;

            foreach (var source in menus.Take(5))
            {
                var menu = new SelectMenuBuilder()
                    .WithCustomId(source.CustomId)
                    .WithPlaceholder(DiscordLimits.Truncate(source.Placeholder, 150))
                    .WithMinValues(source.MinValues);

                if (source.Kind == SelectMenuKind.User)
                {
                    menu.WithType(ComponentType.UserSelect).WithMaxValues(source.MaxValues);
                }
                else
                {
                    menu.WithMaxValues(Math.Min(source.MaxValues, source.Options.Count));

                    foreach (var option in source.Options.Take(DiscordLimits.ChoicesPerMenu))
                    {
                        var label = DiscordLimits.Truncate(option.Label, DiscordLimits.OptionLabelLength);
                        menu.AddOption(label, option.Value, DiscordLimits.Truncate(option.Description, DiscordLimits.OptionLabelLength), isDefault: option.Default);
                    }
                }

                builder.WithSelectMenu(menu, row++);
            }

            var index = 0;

            foreach (var button in buttons.Take((5 - row) * 5))
            {
                builder.WithButton(DiscordLimits.Truncate(button.Label, 80), button.CustomId, Map(button.Style), disabled: button.Disabled, row: row + (index++ / 5));
            }

            return builder.Build();
        }

        // Cards need the newer layout's flag; a message with cards has no text or embeds of its own.
        public static MessageFlags Flags(InteractionReply reply) => reply.IsCards ? MessageFlags.ComponentsV2 : MessageFlags.None;

        public static string Text(InteractionReply reply) => reply.IsCards ? null : reply.Content;

        private static MessageComponent CardComponents(InteractionReply reply)
        {
            var builder = new ComponentBuilderV2();

            foreach (var card in reply.Cards)
            {
                var container = new ContainerBuilder().WithAccentColor(card.Color.HasValue ? new Color(card.Color.Value) : null);
                container.WithTextDisplay(DiscordLimits.Truncate(card.Text, 4000));

                if (card.Buttons.Any())
                {
                    container.WithActionRow(card.Buttons.Take(5).Select(b => (IMessageComponentBuilder)new ButtonBuilder(DiscordLimits.Truncate(b.Label, 80), b.CustomId, Map(b.Style), isDisabled: b.Disabled)).ToList());
                }

                builder.WithContainer(container);
            }

            return builder.Build();
        }

        public static Embed[] Embeds(InteractionReply reply)
        {
            if (reply.IsCards || reply.Embeds == null || !reply.Embeds.Any())
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

                if (e.ImageUrl != null)
                {
                    builder.WithImageUrl(e.ImageUrl);
                }

                if (e.Color.HasValue)
                {
                    builder.WithColor(new Color(e.Color.Value));
                }

                if (e.Footer != null)
                {
                    builder.WithFooter(DiscordLimits.Truncate(e.Footer, 2048), e.FooterIconUrl);
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
