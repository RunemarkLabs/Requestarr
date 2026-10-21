using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Discord
{
    public interface IGuildSetupService
    {
        Task<DiscordGuildConfig> EnsureAsync();
        DiscordGuildConfig GetConfig();
        Task<ulong> GetCategoryWithRoomAsync();

        // The resolved admin role (provisioned ID, existing role by name, or the one Requestarr created).
        ulong? AdminRoleId { get; }

        // The shared role every onboarded member holds; it can read the announcements channel.
        ulong? MembersRoleId { get; }
        ulong? AnnouncementsChannelId { get; }
    }

    // Makes sure the admin channel, the admin feed channel, the members role, the announcements channel and a
    // request-channel category exist. Existing ones are reused (by stored ID, provisioned ID, then name) and only
    // missing ones are created.
    public class GuildSetupService : IGuildSetupService
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordSettingsStore _settings;
        private readonly IDiscordGuildConfigRepository _repository;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public GuildSetupService(IDiscordGuildGateway gateway, IDiscordSettingsStore settings, IDiscordGuildConfigRepository repository, IAuditService audit, Logger logger)
        {
            _gateway = gateway;
            _settings = settings;
            _repository = repository;
            _audit = audit;
            _logger = logger;
        }

        public ulong? AdminRoleId => ulong.TryParse(GetConfig()?.AdminRoleId, out var id) ? id : _settings.Current.AdminRoleId;

        public ulong? MembersRoleId => Parse(GetConfig()?.MembersRoleId);

        public ulong? AnnouncementsChannelId => Parse(GetConfig()?.AnnouncementsChannelId);

        public DiscordGuildConfig GetConfig()
        {
            return _repository.FindByGuild(_gateway.GuildId.ToString());
        }

        public async Task<DiscordGuildConfig> EnsureAsync()
        {
            var settings = _settings.Current;
            var config = GetConfig() ?? new DiscordGuildConfig { GuildId = _gateway.GuildId.ToString() };

            var adminRoleId = await EnsureAdminRoleAsync(settings, config);
            config.AdminRoleId = adminRoleId.ToString();

            var adminChannel = await ExistingOrNull(Parse(config.AdminChannelId)) ??
                               await ExistingOrNull(settings.AdminChannelId) ??
                               await _gateway.FindTextChannelAsync(ChannelLayout.AdminChannelName);

            if (adminChannel == null)
            {
                var overwrites = ChannelLayout.AdminOnly(_gateway.GuildId, _gateway.BotUserId, adminRoleId);
                adminChannel = await _gateway.CreateTextChannelAsync(ChannelLayout.AdminChannelName, null, "Requestarr admin commands and approvals", overwrites);
                _logger.Info("Created Discord admin channel {0}", adminChannel);
                _audit.Record(Actor.System, "discord.admin-channel.create", "channel", adminChannel.ToString());
            }

            await EnsureAdminAccess(adminChannel.Value, adminRoleId);
            config.AdminChannelId = adminChannel.ToString();
            config.AdminQueueChannelId = adminChannel.ToString();

            // The admin feed sits next to the admin channel; a new (or different) one is seeded again.
            var feedChannel = await ExistingOrNull(Parse(config.FeedChannelId)) ?? await _gateway.FindTextChannelAsync(ChannelLayout.FeedChannelName);

            if (feedChannel == null)
            {
                var overwrites = ChannelLayout.AdminOnly(_gateway.GuildId, _gateway.BotUserId, adminRoleId);
                feedChannel = await _gateway.CreateTextChannelAsync(ChannelLayout.FeedChannelName, null, DiscordText.FeedChannelTopic, overwrites);
                _logger.Info("Created Discord admin feed channel {0}", feedChannel);
                _audit.Record(Actor.System, "discord.feed-channel.create", "channel", feedChannel.ToString());
            }

            await EnsureAdminAccess(feedChannel.Value, adminRoleId);

            if (config.FeedChannelId != feedChannel.ToString())
            {
                config.FeedChannelId = feedChannel.ToString();
                config.FeedSeeded = false;
            }

            // One shared role for every member (it costs one of the server's 250 roles, not one each) reads the
            // announcements channel; only admins and the bot post there.
            var membersRole = await EnsureMembersRoleAsync(config);
            config.MembersRoleId = membersRole.ToString();
            var announcementsLayout = ChannelLayout.Announcements(_gateway.GuildId, _gateway.BotUserId, adminRoleId, membersRole);
            var announcements = await ExistingOrNull(Parse(config.AnnouncementsChannelId)) ?? await _gateway.FindTextChannelAsync(ChannelLayout.AnnouncementsChannelName);

            if (announcements == null)
            {
                announcements = await _gateway.CreateTextChannelAsync(ChannelLayout.AnnouncementsChannelName, null, DiscordText.AnnouncementsChannelTopic, announcementsLayout);
                _logger.Info("Created Discord announcements channel {0}", announcements);
                _audit.Record(Actor.System, "discord.announcements-channel.create", "channel", announcements.ToString());
            }
            else
            {
                await EnsureOverwrites(announcements.Value, announcementsLayout.Where(o => o.TargetId != _gateway.GuildId && o.TargetId != _gateway.BotUserId));
            }

            config.AnnouncementsChannelId = announcements.ToString();

            var categories = new List<string>();

            foreach (var id in config.RequestCategoryIds ?? new List<string>())
            {
                if (await ExistingOrNull(Parse(id)) != null)
                {
                    await EnsureAdminAccess(ulong.Parse(id), adminRoleId);
                    categories.Add(id);
                }
            }

            if (!categories.Any())
            {
                var category = await ExistingOrNull(settings.RequestCategoryId) ?? await _gateway.FindCategoryAsync(ChannelLayout.RequestCategoryName);

                if (category == null)
                {
                    category = await CreateCategoryAsync(1);
                }
                else
                {
                    await EnsureAdminAccess(category.Value, adminRoleId);
                }

                categories.Add(category.ToString());
            }

            config.RequestCategoryIds = categories;
            _repository.Upsert(config);

            return config;
        }

        // Request channels spill into a new category once one holds Discord's 50-channel maximum.
        public async Task<ulong> GetCategoryWithRoomAsync()
        {
            var config = GetConfig() ?? await EnsureAsync();

            foreach (var id in config.RequestCategoryIds)
            {
                var categoryId = ulong.Parse(id);

                if (await _gateway.ChannelExistsAsync(categoryId) &&
                    await _gateway.GetChannelCountInCategoryAsync(categoryId) < DiscordLimits.ChannelsPerCategory)
                {
                    return categoryId;
                }
            }

            var created = await CreateCategoryAsync(config.RequestCategoryIds.Count + 1);
            config.RequestCategoryIds.Add(created.ToString());
            _repository.Upsert(config);

            return created;
        }

        private async Task<ulong> CreateCategoryAsync(int index)
        {
            var name = ChannelLayout.CategoryName(index);
            var id = await _gateway.CreateCategoryAsync(name, ChannelLayout.AdminOnly(_gateway.GuildId, _gateway.BotUserId, AdminRoleId));

            _logger.Info("Created Discord category '{0}' ({1})", name, id);
            _audit.Record(Actor.System, "discord.category.create", "channel", id.ToString(), name);

            return id;
        }

        // Provisioned role ID if it exists, else a role with the configured name, else create it.
        // New roles are created at the bottom of the role list, below the bot's own role.
        private async Task<ulong> EnsureAdminRoleAsync(DiscordSettings settings, DiscordGuildConfig config)
        {
            if (settings.AdminRoleId.HasValue && await _gateway.RoleExistsAsync(settings.AdminRoleId.Value))
            {
                return settings.AdminRoleId.Value;
            }

            if (ulong.TryParse(config.AdminRoleId, out var stored) && await _gateway.RoleExistsAsync(stored))
            {
                return stored;
            }

            var name = string.IsNullOrWhiteSpace(settings.AdminRoleName) ? DiscordSettings.DefaultAdminRoleName : settings.AdminRoleName;
            var existing = await _gateway.FindRoleAsync(name);

            if (existing.HasValue)
            {
                return existing.Value;
            }

            var created = await _gateway.CreateRoleAsync(name);
            _logger.Info("Created Discord admin role '{0}' ({1})", name, created);
            _audit.Record(Actor.System, "discord.admin-role.create", "role", created.ToString(), name);

            return created;
        }

        private async Task<ulong> EnsureMembersRoleAsync(DiscordGuildConfig config)
        {
            if (ulong.TryParse(config.MembersRoleId, out var stored) && await _gateway.RoleExistsAsync(stored))
            {
                return stored;
            }

            var existing = await _gateway.FindRoleAsync(ChannelLayout.MembersRoleName);

            if (existing.HasValue)
            {
                return existing.Value;
            }

            var created = await _gateway.CreateRoleAsync(ChannelLayout.MembersRoleName);
            _logger.Info("Created Discord members role '{0}' ({1})", ChannelLayout.MembersRoleName, created);
            _audit.Record(Actor.System, "discord.members-role.create", "role", created.ToString(), ChannelLayout.MembersRoleName);

            return created;
        }

        // Puts back the given overwrites (by target) on an existing channel, keeping any others.
        private async Task EnsureOverwrites(ulong channelId, IEnumerable<PermissionOverwrite> wanted)
        {
            var overwrites = (await _gateway.GetChannelOverwritesAsync(channelId)).ToList();
            var missing = wanted.Where(w => !overwrites.Contains(w)).ToList();

            if (missing.Count == 0)
            {
                return;
            }

            overwrites.RemoveAll(o => missing.Any(m => m.TargetId == o.TargetId));
            overwrites.AddRange(missing);
            await _gateway.SetChannelOverwritesAsync(channelId, overwrites);
        }

        // Adds the admin role to an admin-only channel or category that lacks it (keeps everything else).
        private async Task EnsureAdminAccess(ulong channelId, ulong adminRoleId)
        {
            var overwrites = await _gateway.GetChannelOverwritesAsync(channelId);

            if (overwrites.Any(o => o.TargetId == adminRoleId))
            {
                return;
            }

            var updated = overwrites.ToList();
            const ChannelPermissions readWrite = ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages | ChannelPermissions.ReadMessageHistory;
            updated.Add(new PermissionOverwrite(adminRoleId, OverwriteTarget.Role, readWrite, ChannelPermissions.None));
            await _gateway.SetChannelOverwritesAsync(channelId, updated);
        }

        private async Task<ulong?> ExistingOrNull(ulong? channelId)
        {
            return channelId.HasValue && await _gateway.ChannelExistsAsync(channelId.Value) ? channelId : null;
        }

        private static ulong? Parse(string id) => ulong.TryParse(id, out var value) ? value : null;
    }
}
