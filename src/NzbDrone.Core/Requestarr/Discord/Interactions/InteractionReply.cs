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
    }

    public class InteractionSelectMenu
    {
        public string CustomId { get; init; }
        public string Placeholder { get; init; }
        public int MinValues { get; init; } = 1;
        public int MaxValues { get; init; } = 1;
        public List<InteractionSelectOption> Options { get; init; } = new();
    }

    public record InteractionSelectOption(string Label, string Value, string Description = null);

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
