using System.Collections.Generic;

namespace Requestarr.Core.Discord.Interactions
{
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Success,
        Danger
    }

    public class InteractionReply
    {
        public string Content { get; init; }
        public bool Ephemeral { get; init; }
        public List<InteractionEmbed> Embeds { get; init; } = new();
        public List<InteractionButton> Buttons { get; init; } = new();
        public InteractionSelectMenu SelectMenu { get; init; }

        // Further menus, each on its own row under SelectMenu (cards with several choices).
        public List<InteractionSelectMenu> MoreMenus { get; init; } = new();

        // Edits only: leave the message's embeds as they are.
        public bool KeepEmbeds { get; init; }

        // Posts only: no mention pings anyone and no notification is sent (a panel moved to the bottom again).
        public bool Quiet { get; init; }

        // Cards instead of Content and Embeds: coloured boxes of normal-size text (headings allowed) with their buttons.
        // Discord shows embed text smaller than a message; cards read like a message, with a colour. At most 4,000
        // characters of text in all of a message's cards.
        public List<InteractionCard> Cards { get; init; } = new();

        public bool IsCards => Cards != null && Cards.Count > 0;
    }

    public class InteractionCard
    {
        public uint? Color { get; init; }
        public string Text { get; init; }
        public List<InteractionButton> Buttons { get; init; } = new();
    }

    // Text: the menu's own options. User: Discord's member picker (values are user IDs).
    public enum SelectMenuKind
    {
        Text,
        User
    }

    public class InteractionSelectMenu
    {
        public SelectMenuKind Kind { get; init; } = SelectMenuKind.Text;
        public string CustomId { get; init; }
        public string Placeholder { get; init; }
        public int MinValues { get; init; } = 1;
        public int MaxValues { get; init; } = 1;
        public List<InteractionSelectOption> Options { get; init; } = new();
    }

    public record InteractionSelectOption(string Label, string Value, string Description = null, bool Default = false);

    public record InteractionChoice(string Name, string Value);

    public class InteractionModal
    {
        public string CustomId { get; init; }
        public string Title { get; init; }
        public List<InteractionTextField> Fields { get; init; } = new();
    }

    public record InteractionTextField(string CustomId, string Label, bool Required = true, int MaxLength = 400, bool Paragraph = true);

    public class InteractionEmbed
    {
        public string Title { get; init; }
        public string Description { get; init; }
        public string Url { get; init; }
        public string ThumbnailUrl { get; init; }

        // Large image under the fields (the poster on a request card).
        public string ImageUrl { get; init; }

        // Side colour as 0xRRGGBB, so separate requests stand apart in the channel.
        public uint? Color { get; init; }
        public string Footer { get; init; }
        public string FooterIconUrl { get; init; }
        public List<KeyValuePair<string, string>> Fields { get; init; } = new();
    }

    public class InteractionButton
    {
        public string CustomId { get; init; }
        public string Label { get; init; }
        public ButtonStyle Style { get; init; } = ButtonStyle.Secondary;
        public bool Disabled { get; init; }
    }
}
