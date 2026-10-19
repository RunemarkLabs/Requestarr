using System.Collections.Generic;
using System.Linq;

namespace Requestarr.Core.Discord.Interactions
{
    public enum InteractionKind
    {
        SlashCommand,
        Button,
        SelectMenu,
        ModalSubmit
    }

    // Library-neutral view of one Discord interaction. The gateway adapter (Phase 1) maps
    // Discord.Net interactions onto this; tests build it directly. It carries no authority:
    // handlers must re-check permissions for UserId against the service layer.
    public class DiscordInteraction
    {
        public InteractionKind Kind { get; init; }

        // Slash commands: full command path, e.g. "request movie" or "user add".
        public string CommandName { get; init; }

        // Components and modals: the custom ID, conventionally "{route}:{arg}:{arg}".
        public string CustomId { get; init; }

        public IReadOnlyDictionary<string, object> Options { get; init; } = new Dictionary<string, object>();

        // Select menus: the chosen values. Modals: field values keyed by field custom ID.
        public IReadOnlyList<string> Values { get; init; } = new List<string>();
        public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

        public ulong UserId { get; init; }
        public string Username { get; init; }
        public string DisplayName { get; init; }
        public IReadOnlyList<ulong> RoleIds { get; init; } = new List<ulong>();
        public ulong GuildId { get; init; }
        public ulong ChannelId { get; init; }

        // Route used for dispatch: the command path, or the custom ID up to the first ':'.
        public string Route => Kind == InteractionKind.SlashCommand
            ? CommandName
            : CustomId?.Split(':')[0];

        // Custom ID arguments after the route.
        public IReadOnlyList<string> CustomIdArgs => CustomId == null
            ? new List<string>()
            : CustomId.Split(':').Skip(1).ToList();

        public T GetOption<T>(string name, T defaultValue = default)
        {
            return Options.TryGetValue(name, out var value) && value is T typed ? typed : defaultValue;
        }
    }
}
