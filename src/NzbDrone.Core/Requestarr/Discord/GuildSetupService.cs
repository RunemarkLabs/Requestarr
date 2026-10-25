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
        ulong? WelcomeChannelId { get; }
        ulong? BlockedRoleId { get; }
    }

    // Makes sure the two channel groups exist with what belongs in them: Requestarr Admin (the admin channel and the
    // feed) and Requestarr Users (the welcome, announcements and blocked channels, then every member's private
    // channel), plus the members and blocked roles. Existing ones are reused (by stored ID, provisioned ID, then
    // name), moved into their group if they're elsewhere (messages kept), and only missing ones are created.
    public class GuildSetupService : IGuildSetupService
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordSettingsStore _settings;
        private readonly IDiscordGuildConfigRepository _repository;
        private readonly Users.IRequestarrUserRepository _users;
        private readonly IAuditService _audit;
        private readonly Announcements.IReleaseEnvironment _environment;
        private readonly Logger _logger;

        public GuildSetupService(IDiscordGuildGateway gateway, IDiscordSettingsStore settings, IDiscordGuildConfigRepository repository, Users.IRequestarrUserRepository users, IAuditService audit, Announcements.IReleaseEnvironment environment, Logger logger)
        {
            _environment = environment;
            _gateway = gateway;
            _settings = settings;
            _repository = repository;
            _users = users;
            _audit = audit;
            _logger = logger;
        }

        public ulong? AdminRoleId => ulong.TryParse(GetConfig()?.AdminRoleId, out var id) ? id : _settings.Current.AdminRoleId;

        public ulong? MembersRoleId => Parse(GetConfig()?.MembersRoleId);

        public ulong? AnnouncementsChannelId => Parse(GetConfig()?.AnnouncementsChannelId);

        public ulong? WelcomeChannelId => Parse(GetConfig()?.WelcomeChannelId);

        public ulong? BlockedRoleId => Parse(GetConfig()?.BlockedRoleId);

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
            var group = ChannelLayout.Group(_gateway.GuildId, _gateway.BotUserId, adminRoleId);

            // The Requestarr Admin group: its permissions are set once, and the admin channel and the feed follow them.
            var adminGroup = await ExistingOrNull(Parse(config.AdminCategoryId)) ?? await _gateway.FindCategoryAsync(ChannelLayout.AdminCategoryName);

            if (adminGroup == null)
            {
                adminGroup = await _gateway.CreateCategoryAsync(ChannelLayout.AdminCategoryName, group);
                _logger.Info("Created Discord group '{0}' ({1})", ChannelLayout.AdminCategoryName, adminGroup);
                _audit.Record(Actor.System, "discord.category.create", "channel", adminGroup.ToString(), ChannelLayout.AdminCategoryName);
            }

            await SyncOverwrites(adminGroup.Value, group);
            config.AdminCategoryId = adminGroup.ToString();

            var adminChannel = await ExistingOrNull(Parse(config.AdminChannelId)) ??
                               await ExistingOrNull(settings.AdminChannelId) ??
                               await _gateway.FindTextChannelAsync(ChannelLayout.AdminChannelName);

            if (adminChannel == null)
            {
                adminChannel = await _gateway.CreateTextChannelAsync(ChannelLayout.AdminChannelName, adminGroup, "Requestarr admin commands and approvals", group);
                _logger.Info("Created Discord admin channel {0}", adminChannel);
                _audit.Record(Actor.System, "discord.admin-channel.create", "channel", adminChannel.ToString());
            }

            await PlaceAsync(adminChannel.Value, adminGroup.Value, group);
            config.AdminChannelId = adminChannel.ToString();
            config.AdminQueueChannelId = adminChannel.ToString();

            // The admin feed sits next to the admin channel; a new (or different) one is seeded again.
            var feedChannel = await ExistingOrNull(Parse(config.FeedChannelId)) ?? await _gateway.FindTextChannelAsync(ChannelLayout.FeedChannelName);

            if (feedChannel == null)
            {
                feedChannel = await _gateway.CreateTextChannelAsync(ChannelLayout.FeedChannelName, adminGroup, DiscordText.FeedChannelTopic, group);
                _logger.Info("Created Discord admin feed channel {0}", feedChannel);
                _audit.Record(Actor.System, "discord.feed-channel.create", "channel", feedChannel.ToString());
            }

            await PlaceAsync(feedChannel.Value, adminGroup.Value, group);

            if (config.FeedChannelId != feedChannel.ToString())
            {
                config.FeedChannelId = feedChannel.ToString();
                config.FeedSeeded = false;
            }

            // QA and dev only: the QA checklist, admin-only, next to the feed. A new (or different) one is posted again.
            if (Qa.QaChecklist.HasChecklist(_environment))
            {
                var checklist = await ExistingOrNull(Parse(config.QaChecklistChannelId)) ?? await _gateway.FindTextChannelAsync(Qa.QaText.ChecklistChannelName);

                if (checklist == null)
                {
                    checklist = await _gateway.CreateTextChannelAsync(Qa.QaText.ChecklistChannelName, adminGroup, Qa.QaText.ChecklistChannelTopic, group);
                    _logger.Info("Created Discord QA checklist channel {0}", checklist);
                    _audit.Record(Actor.System, "discord.qa-checklist-channel.create", "channel", checklist.ToString());
                }

                await PlaceAsync(checklist.Value, adminGroup.Value, group);

                if (config.QaChecklistChannelId != checklist.ToString())
                {
                    config.QaChecklistChannelId = checklist.ToString();
                    config.QaChecklistSummaryMessageId = null;
                }
            }

            // One shared role for every member (it costs one of the server's 250 roles, not one each) reads the
            // announcements channel; only admins and the bot post there. Another hides the welcome channel from
            // someone blocked for spamming its buttons.
            var membersRole = await EnsureRoleAsync(config.MembersRoleId, ChannelLayout.MembersRoleName, "discord.members-role.create");
            config.MembersRoleId = membersRole.ToString();
            var blockedRole = await EnsureRoleAsync(config.BlockedRoleId, ChannelLayout.BlockedRoleName, "discord.blocked-role.create");
            config.BlockedRoleId = blockedRole.ToString();

            // The Requestarr Users groups: the request groups from before, renamed in place.
            config.RequestCategoryIds = await EnsureUsersGroupsAsync(config, settings, adminRoleId);
            _repository.Upsert(config);
            var usersGroup = ulong.Parse(config.RequestCategoryIds[0]);

            var welcome = ChannelLayout.Welcome(_gateway.GuildId, _gateway.BotUserId, adminRoleId, blockedRole);
            var announcements = ChannelLayout.Announcements(_gateway.GuildId, _gateway.BotUserId, adminRoleId, membersRole);
            var blocked = ChannelLayout.Blocked(_gateway.GuildId, _gateway.BotUserId, adminRoleId, blockedRole);

            config.WelcomeChannelId = (await EnsureUsersChannelAsync(usersGroup, config.WelcomeChannelId, ChannelLayout.WelcomeChannelName, DiscordText.WelcomeChannelTopic, welcome, "discord.welcome-channel.create")).ToString();
            config.AnnouncementsChannelId = (await EnsureUsersChannelAsync(usersGroup, config.AnnouncementsChannelId, ChannelLayout.AnnouncementsChannelName, DiscordText.AnnouncementsChannelTopic, announcements, "discord.announcements-channel.create")).ToString();
            config.BlockedChannelId = (await EnsureUsersChannelAsync(usersGroup, config.BlockedChannelId, ChannelLayout.BlockedChannelName, DiscordText.BlockedChannelTopic, blocked, "discord.blocked-channel.create")).ToString();

            // Bug reports: members and admins read it and press Report a problem; nobody types in it.
            config.BugReportsChannelId = (await EnsureUsersChannelAsync(usersGroup, config.BugReportsChannelId, Qa.QaText.BugReportsChannelName, Qa.QaText.BugReportsChannelTopic, announcements, "discord.bug-reports-channel.create")).ToString();

            config.RequestCategoryIds = GetConfig()?.RequestCategoryIds ?? config.RequestCategoryIds;
            _repository.Upsert(config);

            // Members' channels left outside the users groups (a group was deleted, so Discord put them at the top level)
            // go back in: the ones Requestarr knows, and any loose one with a request channel's name, live or archived,
            // even when the database doesn't know it (a reset, a new install with the channels kept).
            var known = _users.All().Where(u => u.PrivateChannelId != null).Select(u => u.PrivateChannelId).ToHashSet();
            var textChannels = await _gateway.GetTextChannelsAsync();

            foreach (var channel in textChannels)
            {
                var memberChannel = known.Contains(channel.Id.ToString()) ||
                                    channel.Name.StartsWith(ChannelLayout.UserChannelPrefix) ||
                                    channel.Name.StartsWith(ChannelLayout.ArchivedPrefix + ChannelLayout.UserChannelPrefix);
                var loose = channel.CategoryId == null || !await _gateway.ChannelExistsAsync(channel.CategoryId.Value);

                if (memberChannel && (loose || (known.Contains(channel.Id.ToString()) && !config.RequestCategoryIds.Contains(channel.CategoryId?.ToString()))))
                {
                    await _gateway.MoveChannelAsync(channel.Id, await GetCategoryWithRoomAsync());
                    config = GetConfig();
                    _logger.Info("Moved #{0} back into the {1} group", channel.Name, ChannelLayout.UsersCategoryName);
                }
            }

            // The old request group, if one is left over and empty, goes.
            if (await _gateway.FindCategoryAsync(ChannelLayout.OldRequestCategoryName) is { } old && !config.RequestCategoryIds.Contains(old.ToString()) &&
                await _gateway.GetChannelCountInCategoryAsync(old) == 0)
            {
                await _gateway.DeleteChannelAsync(old);
                _logger.Info("Removed the empty '{0}' group", ChannelLayout.OldRequestCategoryName);
                _audit.Record(Actor.System, "discord.category.delete", "channel", old.ToString(), ChannelLayout.OldRequestCategoryName);
            }

            return config;
        }

        // The users groups in order, each named for its place and admin-only; the first is created if there's none.
        private async Task<List<string>> EnsureUsersGroupsAsync(DiscordGuildConfig config, DiscordSettings settings, ulong adminRoleId)
        {
            var groups = new List<string>();

            foreach (var id in config.RequestCategoryIds ?? new List<string>())
            {
                if (await ExistingOrNull(Parse(id)) is { } existing)
                {
                    groups.Add(existing.ToString());
                }
            }

            if (!groups.Any())
            {
                var found = await ExistingOrNull(settings.RequestCategoryId) ??
                            await _gateway.FindCategoryAsync(ChannelLayout.UsersCategoryName) ??
                            await _gateway.FindCategoryAsync(ChannelLayout.OldRequestCategoryName) ??
                            await CreateCategoryAsync(1);

                groups.Add(found.ToString());
            }

            for (var i = 0; i < groups.Count; i++)
            {
                var id = ulong.Parse(groups[i]);

                // Discord allows only a couple of renames per channel every ten minutes: rename only when needed.
                if (await _gateway.FindCategoryAsync(ChannelLayout.CategoryName(i + 1)) != id)
                {
                    await _gateway.RenameChannelAsync(id, ChannelLayout.CategoryName(i + 1));
                    _logger.Info("Renamed group {0} to '{1}'", id, ChannelLayout.CategoryName(i + 1));
                }

                await SyncOverwrites(id, ChannelLayout.Group(_gateway.GuildId, _gateway.BotUserId, adminRoleId));
            }

            return groups;
        }

        // A channel of the first users group: found again (stored ID, then name) or created, moved into the group if
        // it isn't there (making room if the group is full), with its own permissions.
        private async Task<ulong> EnsureUsersChannelAsync(ulong usersGroup, string storedId, string name, string topic, IReadOnlyList<PermissionOverwrite> layout, string auditAction)
        {
            var channel = await ExistingOrNull(Parse(storedId)) ?? await _gateway.FindTextChannelAsync(name);

            if (channel == null)
            {
                await MakeRoomAsync(usersGroup);
                channel = await _gateway.CreateTextChannelAsync(name, usersGroup, topic, layout);
                _logger.Info("Created Discord channel {0} ({1})", name, channel);
                _audit.Record(Actor.System, auditAction, "channel", channel.ToString());
                return channel.Value;
            }

            if (await _gateway.GetChannelCategoryAsync(channel.Value) != usersGroup)
            {
                await MakeRoomAsync(usersGroup);
                await _gateway.MoveChannelAsync(channel.Value, usersGroup);
                _logger.Info("Moved {0} into the {1} group", name, ChannelLayout.UsersCategoryName);
            }

            await SyncOverwrites(channel.Value, layout);
            return channel.Value;
        }

        // A full first users group makes room: its newest member channel moves to a group with room.
        private async Task MakeRoomAsync(ulong usersGroup)
        {
            if (await _gateway.GetChannelCountInCategoryAsync(usersGroup) < DiscordLimits.ChannelsPerCategory)
            {
                return;
            }

            var channels = _users.All()
                .Select(u => ulong.TryParse(u.PrivateChannelId, out var id) ? id : 0)
                .Where(id => id != 0)
                .OrderByDescending(id => id);

            foreach (var channel in channels)
            {
                if (await _gateway.GetChannelCategoryAsync(channel) == usersGroup)
                {
                    await _gateway.MoveChannelAsync(channel, await GetCategoryWithRoomAsync(usersGroup));
                    return;
                }
            }
        }

        // Puts a channel in its group with exactly the group's permissions.
        private async Task PlaceAsync(ulong channelId, ulong groupId, IReadOnlyList<PermissionOverwrite> group)
        {
            if (await _gateway.GetChannelCategoryAsync(channelId) != groupId)
            {
                await _gateway.MoveChannelAsync(channelId, groupId);
            }

            await SyncOverwrites(channelId, group);
        }

        // Request channels spill into a new category once one holds Discord's 50-channel maximum.
        public Task<ulong> GetCategoryWithRoomAsync() => GetCategoryWithRoomAsync(null);

        private async Task<ulong> GetCategoryWithRoomAsync(ulong? except)
        {
            var config = GetConfig() ?? await EnsureAsync();

            foreach (var id in config.RequestCategoryIds)
            {
                var categoryId = ulong.Parse(id);

                if (categoryId != except && await _gateway.ChannelExistsAsync(categoryId) &&
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

        private async Task<ulong> EnsureRoleAsync(string storedId, string name, string auditAction)
        {
            if (ulong.TryParse(storedId, out var stored) && await _gateway.RoleExistsAsync(stored))
            {
                return stored;
            }

            var existing = await _gateway.FindRoleAsync(name);

            if (existing.HasValue)
            {
                return existing.Value;
            }

            var created = await _gateway.CreateRoleAsync(name);
            _logger.Info("Created Discord role '{0}' ({1})", name, created);
            _audit.Record(Actor.System, auditAction, "role", created.ToString(), name);

            return created;
        }

        // Makes a channel's permissions exactly these (a channel that follows its group, or one Requestarr owns).
        private async Task SyncOverwrites(ulong channelId, IReadOnlyList<PermissionOverwrite> wanted)
        {
            var overwrites = await _gateway.GetChannelOverwritesAsync(channelId);

            if (overwrites.Count != wanted.Count || wanted.Any(w => !overwrites.Contains(w)))
            {
                await _gateway.SetChannelOverwritesAsync(channelId, wanted);
            }
        }

        private async Task<ulong?> ExistingOrNull(ulong? channelId)
        {
            return channelId.HasValue && await _gateway.ChannelExistsAsync(channelId.Value) ? channelId : null;
        }

        private static ulong? Parse(string id) => ulong.TryParse(id, out var value) ? value : null;
    }
}
