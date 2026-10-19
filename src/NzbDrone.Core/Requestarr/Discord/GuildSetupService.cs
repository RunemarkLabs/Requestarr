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
    }

    // Makes sure the admin channel and a request-channel category exist. Existing ones are
    // reused (by stored ID, provisioned ID, then name) and only missing ones are created.
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

        public DiscordGuildConfig GetConfig()
        {
            return _repository.FindByGuild(_gateway.GuildId.ToString());
        }

        public async Task<DiscordGuildConfig> EnsureAsync()
        {
            var settings = _settings.Current;
            var config = GetConfig() ?? new DiscordGuildConfig { GuildId = _gateway.GuildId.ToString() };

            config.AdminRoleId = settings.AdminRoleId?.ToString();
            var adminRoleId = settings.AdminRoleId;

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

            config.AdminChannelId = adminChannel.ToString();
            config.AdminQueueChannelId = adminChannel.ToString();

            var categories = new List<string>();

            foreach (var id in config.RequestCategoryIds ?? new List<string>())
            {
                if (await ExistingOrNull(Parse(id)) != null)
                {
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
            var id = await _gateway.CreateCategoryAsync(name, ChannelLayout.AdminOnly(_gateway.GuildId, _gateway.BotUserId, _settings.Current.AdminRoleId));

            _logger.Info("Created Discord category '{0}' ({1})", name, id);
            _audit.Record(Actor.System, "discord.category.create", "channel", id.ToString(), name);

            return id;
        }

        private async Task<ulong?> ExistingOrNull(ulong? channelId)
        {
            return channelId.HasValue && await _gateway.ChannelExistsAsync(channelId.Value) ? channelId : null;
        }

        private static ulong? Parse(string id) => ulong.TryParse(id, out var value) ? value : null;
    }
}
