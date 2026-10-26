using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Discord.Gateway
{
    public record GuildMember(ulong Id, string Username, string DisplayName, string AvatarUrl, bool IsBot, IReadOnlyList<ulong> RoleIds);

    [Flags]
    public enum ChannelPermissions
    {
        None = 0,
        ViewChannel = 1,
        SendMessages = 2,
        ReadMessageHistory = 4,
        EmbedLinks = 8,

        // Creating public or private threads and posting in them.
        Threads = 16
    }

    public enum OverwriteTarget
    {
        Role,
        Member
    }

    // A text channel in the server: its name and the group it's in (null at the top level).
    public record GuildTextChannel(ulong Id, string Name, ulong? CategoryId);

    public record PermissionOverwrite(ulong TargetId, OverwriteTarget TargetType, ChannelPermissions Allow, ChannelPermissions Deny);

    // A message as Requestarr reads it back: its text, its first embed's title and description (version cards), and
    // its buttons' and menus' custom IDs (which panel it is).
    // Labels: its buttons' labels, in order. IsCards: it uses the newer layout (cards).
    public record ChannelMessage(ulong Id, string Content, string EmbedTitle = null, string EmbedDescription = null, IReadOnlyList<string> CustomIds = null, IReadOnlyList<string> Labels = null, bool IsCards = false)
    {
        public bool Has(string route) => CustomIds != null && CustomIds.Any(id => id == route || id.StartsWith(route + ":"));
    }

    // Operations Requestarr performs in its one Discord server. Implemented over Discord.Net
    // for the live bot and in memory for tests, so all rules are testable without Discord.
    public interface IDiscordGuildGateway
    {
        bool IsConnected { get; }

        // Pin Messages (1 << 51) is separate from Manage Messages; without it panels are posted unpinned.
        bool CanPinMessages { get; }
        ulong GuildId { get; }
        ulong BotUserId { get; }
        ulong OwnerId { get; }

        Task<GuildMember> GetMemberAsync(ulong userId);
        Task<int> GetRoleCountAsync();

        Task<bool> ChannelExistsAsync(ulong channelId);
        Task<ulong?> FindCategoryAsync(string name);
        Task<ulong?> FindTextChannelAsync(string name);
        Task<int> GetChannelCountInCategoryAsync(ulong categoryId);

        // Every text channel in the server, from Discord (not the cache).
        Task<IReadOnlyList<GuildTextChannel>> GetTextChannelsAsync();

        // The group (category) a channel sits in; null when it's in none or is gone.
        Task<ulong?> GetChannelCategoryAsync(ulong channelId);

        // Moves a channel into a group, keeping its messages and its own permissions.
        Task MoveChannelAsync(ulong channelId, ulong categoryId);
        Task<ulong> CreateCategoryAsync(string name, IReadOnlyList<PermissionOverwrite> overwrites);
        Task<ulong> CreateTextChannelAsync(string name, ulong? categoryId, string topic, IReadOnlyList<PermissionOverwrite> overwrites);
        Task SetChannelOverwritesAsync(ulong channelId, IReadOnlyList<PermissionOverwrite> overwrites);
        Task<IReadOnlyList<PermissionOverwrite>> GetChannelOverwritesAsync(ulong channelId);
        Task RenameChannelAsync(ulong channelId, string name);
        Task DeleteChannelAsync(ulong channelId);

        Task<ulong> CreateRoleAsync(string name);
        Task<bool> RoleExistsAsync(ulong roleId);
        Task<ulong?> FindRoleAsync(string name);
        Task DeleteRoleAsync(ulong roleId);
        Task AddRoleToMemberAsync(ulong userId, ulong roleId);
        Task RemoveRoleFromMemberAsync(ulong userId, ulong roleId);

        Task<ulong> SendMessageAsync(ulong channelId, InteractionReply message);

        // False when the message no longer exists (deleted by someone).
        Task<bool> EditMessageAsync(ulong channelId, ulong messageId, InteractionReply message);

        // Pins a message and removes Discord's "pinned a message" notice, so pins don't clutter the channel.
        Task PinMessageAsync(ulong channelId, ulong messageId);
        Task UnpinMessageAsync(ulong channelId, ulong messageId);

        // A private message to a member; false when Discord refuses (they don't accept messages from the server).
        Task<bool> SendDirectMessageAsync(ulong userId, InteractionReply message);

        // The newest message in a channel, whoever wrote it; null when it's empty or gone.
        Task<ulong?> GetLatestMessageIdAsync(ulong channelId);

        // False when the message is gone already or can't be deleted.
        Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId);

        // The bot's own messages among the channel's newest `limit`, oldest first; empty when the channel is gone.
        Task<IReadOnlyList<ChannelMessage>> GetOwnMessagesAsync(ulong channelId, int limit);
    }
}
