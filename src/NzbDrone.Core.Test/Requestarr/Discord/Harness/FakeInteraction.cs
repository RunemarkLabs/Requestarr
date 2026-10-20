using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Discord.Harness
{
    // Builds simulated Discord interactions for handler tests. No Discord connection or user
    // account is involved: the interaction goes straight to the dispatcher, as the gateway
    // adapter would hand it over.
    public class FakeInteraction
    {
        public const ulong DefaultGuildId = 900000000000000001;
        public const ulong DefaultUserId = 900000000000000100;
        public const ulong DefaultChannelId = 900000000000000200;

        private readonly InteractionKind _kind;
        private readonly string _commandName;
        private readonly string _customId;
        private readonly Dictionary<string, object> _options = new();
        private readonly Dictionary<string, string> _fields = new();
        private readonly List<string> _values = new();
        private readonly List<ulong> _roleIds = new();
        private ulong _userId = DefaultUserId;
        private string _username = "test-user";
        private string _displayName = "Test User";
        private ulong _guildId = DefaultGuildId;
        private ulong _channelId = DefaultChannelId;
        private string _focused;
        private ulong? _messageId;

        private FakeInteraction(InteractionKind kind, string commandName, string customId)
        {
            _kind = kind;
            _commandName = commandName;
            _customId = customId;
        }

        public static FakeInteraction SlashCommand(string commandPath) => new(InteractionKind.SlashCommand, commandPath.TrimStart('/'), null);

        public static FakeInteraction Button(string customId) => new(InteractionKind.Button, null, customId);

        public static FakeInteraction SelectMenu(string customId, params string[] values) => new FakeInteraction(InteractionKind.SelectMenu, null, customId).WithValues(values);

        public static FakeInteraction ModalSubmit(string customId) => new(InteractionKind.ModalSubmit, null, customId);

        // Autocomplete for a command option: the text typed so far is the option's value.
        public static FakeInteraction Autocomplete(string commandPath, string focusedOption, string typed) =>
            new FakeInteraction(InteractionKind.Autocomplete, commandPath, null) { _focused = focusedOption }.WithOption(focusedOption, typed);

        public FakeInteraction WithOption(string name, object value)
        {
            _options[name] = value;
            return this;
        }

        public FakeInteraction WithField(string fieldId, string value)
        {
            _fields[fieldId] = value;
            return this;
        }

        public FakeInteraction WithValues(params string[] values)
        {
            _values.AddRange(values);
            return this;
        }

        public FakeInteraction By(ulong userId, string username = null, string displayName = null)
        {
            _userId = userId;
            _username = username ?? $"user-{userId}";
            _displayName = displayName ?? _username;
            return this;
        }

        public FakeInteraction WithRoles(params ulong[] roleIds)
        {
            _roleIds.AddRange(roleIds);
            return this;
        }

        public FakeInteraction InChannel(ulong channelId)
        {
            _channelId = channelId;
            return this;
        }

        // The message the component sits on (so edits land on a real fake message).
        public FakeInteraction OnMessage(ulong? messageId)
        {
            _messageId = messageId;
            return this;
        }

        public FakeInteraction InGuild(ulong guildId)
        {
            _guildId = guildId;
            return this;
        }

        public DiscordInteraction Build()
        {
            return new DiscordInteraction
            {
                Kind = _kind,
                CommandName = _commandName,
                FocusedOption = _focused,
                CustomId = _customId,
                Options = new Dictionary<string, object>(_options),
                Fields = new Dictionary<string, string>(_fields),
                Values = _values.ToList(),
                UserId = _userId,
                Username = _username,
                DisplayName = _displayName,
                RoleIds = _roleIds.ToList(),
                GuildId = _guildId,
                ChannelId = _channelId,
                MessageId = _messageId
            };
        }

        public static implicit operator DiscordInteraction(FakeInteraction builder) => builder.Build();
    }
}
