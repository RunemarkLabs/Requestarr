using System;
using System.Collections.Generic;
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
        EmbedLinks = 8
    }

    public enum OverwriteTarget
    {
        Role,
        Member
    }

    public record PermissionOverwrite(ulong TargetId, OverwriteTarget TargetType, ChannelPermissions Allow, ChannelPermissions Deny);

    // Operations Requestarr performs in its one Discord server. Implemented over Discord.Net
    // for the live bot and in memory for tests, so all rules are testable without Discord.
    public interface IDiscordGuildGateway
    {
        bool IsConnected { get; }
        ulong GuildId { get; }
        ulong BotUserId { get; }
        ulong OwnerId { get; }

        Task<GuildMember> GetMemberAsync(ulong userId);
        Task<int> GetRoleCountAsync();

        Task<bool> ChannelExistsAsync(ulong channelId);
        Task<ulong?> FindCategoryAsync(string name);
        Task<ulong?> FindTextChannelAsync(string name);
        Task<int> GetChannelCountInCategoryAsync(ulong categoryId);
        Task<ulong> CreateCategoryAsync(string name, IReadOnlyList<PermissionOverwrite> overwrites);
        Task<ulong> CreateTextChannelAsync(string name, ulong? categoryId, string topic, IReadOnlyList<PermissionOverwrite> overwrites);
        Task SetChannelOverwritesAsync(ulong channelId, IReadOnlyList<PermissionOverwrite> overwrites);
        Task<IReadOnlyList<PermissionOverwrite>> GetChannelOverwritesAsync(ulong channelId);
        Task RenameChannelAsync(ulong channelId, string name);
        Task DeleteChannelAsync(ulong channelId);

        Task<ulong> CreateRoleAsync(string name);
        Task DeleteRoleAsync(ulong roleId);
        Task AddRoleToMemberAsync(ulong userId, ulong roleId);

        Task<ulong> SendMessageAsync(ulong channelId, InteractionReply message);
    }
}
