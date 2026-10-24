using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Discord.Harness
{
    // In-memory Discord server: roles, channels, categories, members, overwrites, and posted
    // messages, so services and handlers can be tested end to end without Discord.
    public class FakeDiscordGuild : IDiscordGuildGateway
    {
        public const ulong Guild = 900000000000000001;
        public const ulong Owner = 900000000000000010;
        public const ulong Bot = 900000000000000020;

        private ulong _nextId = 910000000000000000;

        public class FakeChannel
        {
            public ulong Id { get; init; }
            public string Name { get; set; }
            public bool IsCategory { get; init; }
            public ulong? CategoryId { get; init; }
            public string Topic { get; init; }
            public List<PermissionOverwrite> Overwrites { get; set; } = new();
            public List<InteractionReply> Messages { get; } = new();
            public List<ulong> MessageIds { get; } = new();
            public HashSet<ulong> Pinned { get; } = new();

            public InteractionReply PinnedMessage => Pinned.Count == 1 ? Messages[MessageIds.IndexOf(Pinned.Single())] : null;
        }

        public Dictionary<ulong, string> Roles { get; } = new();
        public Dictionary<ulong, FakeChannel> Channels { get; } = new();
        public Dictionary<ulong, GuildMember> Members { get; } = new();

        public bool IsConnected { get; set; } = true;
        public ulong GuildId => Guild;
        public ulong BotUserId => Bot;
        public ulong OwnerId => Owner;

        // Fails the next channel creation, to exercise rollback.
        public bool FailNextChannelCreate { get; set; }

        // Simulates a bot without the Pin Messages permission.
        public bool CannotPin { get; set; }

        public bool CanPinMessages => !CannotPin;

        public FakeDiscordGuild()
        {
            AddMember(Owner, "owner");
            AddMember(Bot, "requestarr-bot", isBot: true);
        }

        public GuildMember AddMember(ulong id, string username, bool isBot = false, params ulong[] roles)
        {
            var member = new GuildMember(id, username, username, null, isBot, roles.ToList());
            Members[id] = member;
            return member;
        }

        public void RemoveMember(ulong id) => Members.Remove(id);

        public ulong AddExistingRoles(int count)
        {
            ulong last = 0;

            for (var i = 0; i < count; i++)
            {
                last = NextId();
                Roles[last] = $"existing-{i}";
            }

            return last;
        }

        public FakeChannel ChannelNamed(string name) => Channels.Values.SingleOrDefault(c => c.Name == name);

        public IEnumerable<FakeChannel> ChannelsIn(ulong categoryId) => Channels.Values.Where(c => c.CategoryId == categoryId);

        public Task<GuildMember> GetMemberAsync(ulong userId) => Task.FromResult(Members.TryGetValue(userId, out var m) ? m : null);

        public Task<int> GetRoleCountAsync() => Task.FromResult(Roles.Count + 1); // + @everyone

        public Task<bool> ChannelExistsAsync(ulong channelId) => Task.FromResult(Channels.ContainsKey(channelId));

        public Task<ulong?> FindCategoryAsync(string name) =>
            Task.FromResult(Channels.Values.FirstOrDefault(c => c.IsCategory && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id);

        public Task<ulong?> FindTextChannelAsync(string name) =>
            Task.FromResult(Channels.Values.FirstOrDefault(c => !c.IsCategory && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id);

        public Task<int> GetChannelCountInCategoryAsync(ulong categoryId) => Task.FromResult(ChannelsIn(categoryId).Count());

        public Task<ulong> CreateCategoryAsync(string name, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            var id = NextId();
            Channels[id] = new FakeChannel { Id = id, Name = name, IsCategory = true, Overwrites = overwrites.ToList() };
            return Task.FromResult(id);
        }

        public Task<ulong> CreateTextChannelAsync(string name, ulong? categoryId, string topic, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            if (FailNextChannelCreate)
            {
                FailNextChannelCreate = false;
                throw new InvalidOperationException("Simulated Discord failure");
            }

            if (categoryId.HasValue && ChannelsIn(categoryId.Value).Count() >= 50)
            {
                throw new InvalidOperationException("Discord: maximum number of channels in category reached (50)");
            }

            var id = NextId();
            Channels[id] = new FakeChannel { Id = id, Name = name, CategoryId = categoryId, Topic = topic, Overwrites = overwrites.ToList() };
            return Task.FromResult(id);
        }

        public Task SetChannelOverwritesAsync(ulong channelId, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            Channels[channelId].Overwrites = overwrites.ToList();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PermissionOverwrite>> GetChannelOverwritesAsync(ulong channelId) =>
            Task.FromResult<IReadOnlyList<PermissionOverwrite>>(Channels[channelId].Overwrites.ToList());

        public Task RenameChannelAsync(ulong channelId, string name)
        {
            Channels[channelId].Name = name;
            return Task.CompletedTask;
        }

        public Task DeleteChannelAsync(ulong channelId)
        {
            Channels.Remove(channelId);
            return Task.CompletedTask;
        }

        public Task<ulong> CreateRoleAsync(string name)
        {
            var id = NextId();
            Roles[id] = name;
            return Task.FromResult(id);
        }

        public Task<bool> RoleExistsAsync(ulong roleId) => Task.FromResult(Roles.ContainsKey(roleId));

        public Task<ulong?> FindRoleAsync(string name) =>
            Task.FromResult<ulong?>(Roles.Where(r => string.Equals(r.Value, name, StringComparison.OrdinalIgnoreCase)).Select(r => (ulong?)r.Key).FirstOrDefault());

        public Task DeleteRoleAsync(ulong roleId)
        {
            Roles.Remove(roleId);

            foreach (var member in Members.Values.ToList())
            {
                if (member.RoleIds.Contains(roleId))
                {
                    Members[member.Id] = member with { RoleIds = member.RoleIds.Where(r => r != roleId).ToList() };
                }
            }

            return Task.CompletedTask;
        }

        public Task AddRoleToMemberAsync(ulong userId, ulong roleId)
        {
            var member = Members[userId];
            Members[userId] = member with { RoleIds = member.RoleIds.Append(roleId).Distinct().ToList() };
            return Task.CompletedTask;
        }

        public Task RemoveRoleFromMemberAsync(ulong userId, ulong roleId)
        {
            if (Members.TryGetValue(userId, out var member))
            {
                Members[userId] = member with { RoleIds = member.RoleIds.Where(r => r != roleId).ToList() };
            }

            return Task.CompletedTask;
        }

        public Task<ulong> SendMessageAsync(ulong channelId, InteractionReply message)
        {
            var id = NextId();
            Channels[channelId].Messages.Add(message);
            Channels[channelId].MessageIds.Add(id);
            return Task.FromResult(id);
        }

        public Task<bool> EditMessageAsync(ulong channelId, ulong messageId, InteractionReply message)
        {
            var index = Channels.TryGetValue(channelId, out var channel) ? channel.MessageIds.IndexOf(messageId) : -1;

            if (index < 0)
            {
                return Task.FromResult(false);
            }

            channel.Messages[index] = message.KeepEmbeds
                ? new InteractionReply { Content = message.Content, Buttons = message.Buttons, SelectMenu = message.SelectMenu, MoreMenus = message.MoreMenus, Ephemeral = message.Ephemeral, Embeds = channel.Messages[index].Embeds }
                : message;
            return Task.FromResult(true);
        }

        public Task PinMessageAsync(ulong channelId, ulong messageId)
        {
            if (CannotPin)
            {
                throw new InvalidOperationException("Missing Permissions");
            }

            Channels[channelId].Pinned.Add(messageId);
            return Task.CompletedTask;
        }

        // Simulates a message the bot can't delete (missing permission).
        public bool CannotDelete { get; set; }

        public Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
        {
            if (CannotDelete || !Channels.TryGetValue(channelId, out var channel) || !channel.MessageIds.Contains(messageId))
            {
                return Task.FromResult(false);
            }

            DeleteMessage(channelId, messageId);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ChannelMessage>> GetOwnMessagesAsync(ulong channelId, int limit)
        {
            IReadOnlyList<ChannelMessage> messages = Channels.TryGetValue(channelId, out var channel)
                ? channel.MessageIds.Zip(channel.Messages, (id, m) => new ChannelMessage(id, m.Content)).TakeLast(limit).ToList()
                : new List<ChannelMessage>();
            return Task.FromResult(messages);
        }

        public void DeleteMessage(ulong channelId, ulong messageId)
        {
            var channel = Channels[channelId];
            var index = channel.MessageIds.IndexOf(messageId);
            channel.Messages.RemoveAt(index);
            channel.MessageIds.RemoveAt(index);
            channel.Pinned.Remove(messageId);
        }

        private ulong NextId() => ++_nextId;
    }
}
