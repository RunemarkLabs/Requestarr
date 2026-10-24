using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Discord.Gateway
{
    // IDiscordGuildGateway over Discord.Net. Reads that matter for correctness (members,
    // overwrites) go through REST so they reflect Discord's current state, not the cache.
    public class DiscordNetGateway : IDiscordGuildGateway
    {
        private const ulong ThreadPermissions = (ulong)(ChannelPermission.CreatePublicThreads | ChannelPermission.CreatePrivateThreads | ChannelPermission.SendMessagesInThreads);

        private readonly IDiscordClientHost _host;
        private readonly IDiscordSettingsStore _settings;

        public DiscordNetGateway(IDiscordClientHost host, IDiscordSettingsStore settings)
        {
            _host = host;
            _settings = settings;
        }

        public bool IsConnected => _host.State == DiscordBotState.Connected && Guild != null;
        public bool CanPinMessages => Guild?.CurrentUser?.GuildPermissions.PinMessages ?? false;
        public ulong GuildId => _settings.Current.GuildId;
        public ulong BotUserId => Client.CurrentUser?.Id ?? 0;
        public ulong OwnerId => Guild?.OwnerId ?? 0;

        private DiscordSocketClient Client => _host.Client ?? throw new InvalidOperationException(DiscordText.NotConnected);
        private SocketGuild Guild => _host.Client?.GetGuild(GuildId);

        public async Task<GuildMember> GetMemberAsync(ulong userId)
        {
            var member = await Client.Rest.GetGuildUserAsync(GuildId, userId);

            return member == null
                ? null
                : new GuildMember(member.Id, member.Username, member.DisplayName ?? member.GlobalName ?? member.Username, member.GetDisplayAvatarUrl(), member.IsBot, member.RoleIds.ToList());
        }

        public Task<int> GetRoleCountAsync() => Task.FromResult(RequireGuild().Roles.Count);

        public async Task<bool> ChannelExistsAsync(ulong channelId)
        {
            return await Client.Rest.GetChannelAsync(channelId) is RestGuildChannel channel && channel.GuildId == GuildId;
        }

        public Task<ulong?> FindCategoryAsync(string name) =>
            Task.FromResult(RequireGuild().CategoryChannels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id);

        public Task<ulong?> FindTextChannelAsync(string name) =>
            Task.FromResult(RequireGuild().TextChannels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id);

        public async Task<int> GetChannelCountInCategoryAsync(ulong categoryId)
        {
            var channels = await Client.Rest.GetGuildAsync(GuildId).ContinueWith(t => t.Result.GetChannelsAsync()).Unwrap();

            return channels.OfType<INestedChannel>().Count(c => c.CategoryId == categoryId);
        }

        public async Task<ulong> CreateCategoryAsync(string name, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            var category = await RequireGuild().CreateCategoryChannelAsync(name, p => p.PermissionOverwrites = Map(overwrites));
            return category.Id;
        }

        public async Task<ulong> CreateTextChannelAsync(string name, ulong? categoryId, string topic, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            var channel = await RequireGuild().CreateTextChannelAsync(name, p =>
            {
                p.CategoryId = categoryId.HasValue ? categoryId.Value : Optional<ulong?>.Unspecified;
                p.Topic = topic;
                p.PermissionOverwrites = Map(overwrites);
            });

            return channel.Id;
        }

        public async Task SetChannelOverwritesAsync(ulong channelId, IReadOnlyList<PermissionOverwrite> overwrites)
        {
            var channel = await GetGuildChannel(channelId);
            await channel.ModifyAsync(p => p.PermissionOverwrites = Map(overwrites));
        }

        public async Task<IReadOnlyList<PermissionOverwrite>> GetChannelOverwritesAsync(ulong channelId)
        {
            var channel = await GetGuildChannel(channelId);

            return channel.PermissionOverwrites
                .Select(o => new PermissionOverwrite(o.TargetId,
                    o.TargetType == PermissionTarget.Role ? OverwriteTarget.Role : OverwriteTarget.Member,
                    FromDiscord(o.Permissions.AllowValue),
                    FromDiscord(o.Permissions.DenyValue)))
                .ToList();
        }

        public async Task RenameChannelAsync(ulong channelId, string name)
        {
            var channel = await GetGuildChannel(channelId);
            await channel.ModifyAsync(p => p.Name = name);
        }

        public async Task DeleteChannelAsync(ulong channelId)
        {
            var channel = await GetGuildChannel(channelId);
            await channel.DeleteAsync();
        }

        public async Task<ulong> CreateRoleAsync(string name)
        {
            var role = await RequireGuild().CreateRoleAsync(name, GuildPermissions.None, null, false, false);
            return role.Id;
        }

        public async Task<bool> RoleExistsAsync(ulong roleId)
        {
            var guild = await Client.Rest.GetGuildAsync(GuildId);
            return guild.GetRole(roleId) != null;
        }

        public async Task<ulong?> FindRoleAsync(string name)
        {
            var guild = await Client.Rest.GetGuildAsync(GuildId);
            return guild.Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        public async Task DeleteRoleAsync(ulong roleId)
        {
            var guild = await Client.Rest.GetGuildAsync(GuildId);
            var role = guild.GetRole(roleId);

            if (role != null)
            {
                await role.DeleteAsync();
            }
        }

        public async Task AddRoleToMemberAsync(ulong userId, ulong roleId)
        {
            await Client.Rest.AddRoleAsync(GuildId, userId, roleId);
        }

        public async Task RemoveRoleFromMemberAsync(ulong userId, ulong roleId)
        {
            await Client.Rest.RemoveRoleAsync(GuildId, userId, roleId);
        }

        public async Task<ulong> SendMessageAsync(ulong channelId, InteractionReply message)
        {
            if (await Client.Rest.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                throw new InvalidOperationException($"Channel {channelId} is not a text channel");
            }

            var files = DiscordNetResponder.Files(message);

            try
            {
                var text = DiscordLimits.Truncate(message.Content, DiscordLimits.MessageLength);
                var sent = files.Count == 0
                    ? await channel.SendMessageAsync(text, embeds: DiscordNetResponder.Embeds(message), components: DiscordNetResponder.Components(message), allowedMentions: new AllowedMentions(AllowedMentionTypes.Users))
                    : await channel.SendFilesAsync(files, text, embeds: DiscordNetResponder.Embeds(message), components: DiscordNetResponder.Components(message), allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));

                return sent.Id;
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }
        }

        public async Task<bool> EditMessageAsync(ulong channelId, ulong messageId, InteractionReply message)
        {
            if (await Client.Rest.GetChannelAsync(channelId) is not IMessageChannel channel ||
                await channel.GetMessageAsync(messageId) is not IUserMessage existing)
            {
                return false;
            }

            var files = message.KeepEmbeds ? new List<FileAttachment>() : DiscordNetResponder.Files(message);

            try
            {
                await existing.ModifyAsync(m =>
                {
                    m.Content = DiscordLimits.Truncate(message.Content, DiscordLimits.MessageLength);

                    if (!message.KeepEmbeds)
                    {
                        m.Embeds = DiscordNetResponder.Embeds(message) ?? Array.Empty<Embed>();

                        // The files the new embeds point to, replacing any earlier ones.
                        m.Attachments = files;
                    }

                    m.Components = DiscordNetResponder.Components(message) ?? new ComponentBuilder().Build();
                });
            }
            finally
            {
                files.ForEach(f => f.Dispose());
            }

            return true;
        }

        public async Task<bool> DeleteMessageAsync(ulong channelId, ulong messageId)
        {
            try
            {
                if (await Client.Rest.GetChannelAsync(channelId) is IMessageChannel channel && await channel.GetMessageAsync(messageId) is { } message)
                {
                    await message.DeleteAsync();
                    return true;
                }
            }
            catch (global::Discord.Net.HttpException)
            {
                // Missing permission or already gone: the caller falls back.
            }

            return false;
        }

        public async Task<IReadOnlyList<ChannelMessage>> GetOwnMessagesAsync(ulong channelId, int limit)
        {
            if (await Client.Rest.GetChannelAsync(channelId) is not IMessageChannel channel)
            {
                return Array.Empty<ChannelMessage>();
            }

            // Pages through Discord's 100-message batches; a bot always sees the content of its own messages.
            var messages = await channel.GetMessagesAsync(limit).FlattenAsync();

            return messages.Where(m => m.Author.Id == BotUserId).OrderBy(m => m.Id).Select(m => new ChannelMessage(m.Id, m.Content)).ToList();
        }

        public async Task PinMessageAsync(ulong channelId, ulong messageId)
        {
            if (await Client.Rest.GetChannelAsync(channelId) is IMessageChannel channel &&
                await channel.GetMessageAsync(messageId) is IUserMessage message && !message.IsPinned)
            {
                await message.PinAsync();
            }
        }

        private SocketGuild RequireGuild() => Guild ?? throw new InvalidOperationException(DiscordText.NotConnected);

        private async Task<RestGuildChannel> GetGuildChannel(ulong channelId)
        {
            return await Client.Rest.GetChannelAsync(channelId) as RestGuildChannel ?? throw new InvalidOperationException($"Channel {channelId} not found");
        }

        private static List<Overwrite> Map(IEnumerable<PermissionOverwrite> overwrites)
        {
            return overwrites.Select(o => new Overwrite(o.TargetId,
                o.TargetType == OverwriteTarget.Role ? PermissionTarget.Role : PermissionTarget.User,
                new OverwritePermissions(ToDiscord(o.Allow), ToDiscord(o.Deny)))).ToList();
        }

        private static ulong ToDiscord(ChannelPermissions permissions)
        {
            ulong value = 0;

            if (permissions.HasFlag(ChannelPermissions.ViewChannel))
            {
                value |= (ulong)ChannelPermission.ViewChannel;
            }

            if (permissions.HasFlag(ChannelPermissions.SendMessages))
            {
                value |= (ulong)ChannelPermission.SendMessages;
            }

            if (permissions.HasFlag(ChannelPermissions.ReadMessageHistory))
            {
                value |= (ulong)ChannelPermission.ReadMessageHistory;
            }

            if (permissions.HasFlag(ChannelPermissions.EmbedLinks))
            {
                value |= (ulong)ChannelPermission.EmbedLinks;
            }

            if (permissions.HasFlag(ChannelPermissions.Threads))
            {
                value |= ThreadPermissions;
            }

            return value;
        }

        private static ChannelPermissions FromDiscord(ulong value)
        {
            var result = ChannelPermissions.None;

            if ((value & (ulong)ChannelPermission.ViewChannel) != 0)
            {
                result |= ChannelPermissions.ViewChannel;
            }

            if ((value & (ulong)ChannelPermission.SendMessages) != 0)
            {
                result |= ChannelPermissions.SendMessages;
            }

            if ((value & (ulong)ChannelPermission.ReadMessageHistory) != 0)
            {
                result |= ChannelPermissions.ReadMessageHistory;
            }

            if ((value & (ulong)ChannelPermission.EmbedLinks) != 0)
            {
                result |= ChannelPermissions.EmbedLinks;
            }

            if ((value & ThreadPermissions) == ThreadPermissions)
            {
                result |= ChannelPermissions.Threads;
            }

            return result;
        }
    }
}
