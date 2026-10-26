using System.Collections.Generic;
using System.Linq;
using System.Text;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Discord
{
    // Names and permission overwrites for everything Requestarr creates in Discord.
    // @everyone's role ID equals the server ID.
    public static class ChannelLayout
    {
        public const string AdminChannelName = "requestarr-admin";
        public const string FeedChannelName = "requestarr-feed";
        public const string AnnouncementsChannelName = "requestarr-announcements";

        // Prefixed so it can't clash with a server's own welcome channel.
        public const string WelcomeChannelName = "requestarr-welcome";
        public const string BlockedChannelName = "requestarr-access-blocked";
        public const string MembersRoleName = "Requestarr Members";
        public const string BlockedRoleName = "Requestarr Blocked";

        // Admin channel and feed; then the welcome, announcements and blocked channels and every member's private
        // channel, spilling into "Requestarr Users 2", "3" and on. Before task 4e the users groups were the request
        // groups, holding only the private channels; they are renamed in place, so nothing moves or loses messages.
        public const string AdminCategoryName = "Requestarr Admin";
        public const string UsersCategoryName = "Requestarr Users";
        public const string OldRequestCategoryName = "Requestarr Requests";

        // QA and dev only: the QA checklist's own admin-only group.
        public const string QaCategoryName = "Requestarr QA";
        public const string UserChannelPrefix = "requests-";
        public const string UserRolePrefix = "requestarr-";
        public const string ArchivedPrefix = "archived-";

        private const ChannelPermissions ReadWrite = ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages | ChannelPermissions.ReadMessageHistory;
        private const ChannelPermissions ReadOnly = ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory;
        private const ChannelPermissions NoPosting = ChannelPermissions.SendMessages | ChannelPermissions.Threads;
        private const ChannelPermissions Bot = ReadWrite | ChannelPermissions.EmbedLinks;

        // Both groups: only the admin role and the bot see them. The admin channel and the feed are exactly this
        // (they follow the group); the users groups' channels add their own permissions on top.
        public static IReadOnlyList<PermissionOverwrite> Group(ulong guildId, ulong botUserId, ulong? adminRoleId) => AdminOnly(guildId, botUserId, adminRoleId);

        public static IReadOnlyList<PermissionOverwrite> AdminOnly(ulong guildId, ulong botUserId, ulong? adminRoleId)
        {
            var overwrites = new List<PermissionOverwrite>
            {
                new(guildId, OverwriteTarget.Role, ChannelPermissions.None, ChannelPermissions.ViewChannel),
                new(botUserId, OverwriteTarget.Member, Bot, ChannelPermissions.None)
            };

            if (adminRoleId.HasValue)
            {
                overwrites.Add(new PermissionOverwrite(adminRoleId.Value, OverwriteTarget.Role, ReadWrite, ChannelPermissions.None));
            }

            return overwrites;
        }

        // Every onboarded member (the shared members role) can read it but not post or start threads;
        // admins and the bot post. Hidden from everyone else.
        public static IReadOnlyList<PermissionOverwrite> Announcements(ulong guildId, ulong botUserId, ulong? adminRoleId, ulong membersRoleId)
        {
            var overwrites = AdminOnly(guildId, botUserId, adminRoleId).ToList();
            overwrites.Add(new PermissionOverwrite(membersRoleId, OverwriteTarget.Role, ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory, ChannelPermissions.SendMessages | ChannelPermissions.Threads));

            return overwrites;
        }

        // Everyone in the server reads it; nobody but the bot posts or starts threads. Hidden from the blocked role.
        public static IReadOnlyList<PermissionOverwrite> Welcome(ulong guildId, ulong botUserId, ulong? adminRoleId, ulong blockedRoleId)
        {
            var overwrites = new List<PermissionOverwrite>
            {
                new(guildId, OverwriteTarget.Role, ReadOnly, NoPosting),
                new(botUserId, OverwriteTarget.Member, Bot, ChannelPermissions.None),
                new(blockedRoleId, OverwriteTarget.Role, ChannelPermissions.None, ChannelPermissions.ViewChannel)
            };

            if (adminRoleId.HasValue)
            {
                overwrites.Add(new PermissionOverwrite(adminRoleId.Value, OverwriteTarget.Role, ReadOnly, NoPosting));
            }

            return overwrites;
        }

        // Only the blocked role (and the admins) read it; nobody but the bot posts.
        public static IReadOnlyList<PermissionOverwrite> Blocked(ulong guildId, ulong botUserId, ulong? adminRoleId, ulong blockedRoleId)
        {
            var overwrites = new List<PermissionOverwrite>
            {
                new(guildId, OverwriteTarget.Role, ChannelPermissions.None, ChannelPermissions.ViewChannel),
                new(botUserId, OverwriteTarget.Member, Bot, ChannelPermissions.None),
                new(blockedRoleId, OverwriteTarget.Role, ReadOnly, NoPosting)
            };

            if (adminRoleId.HasValue)
            {
                overwrites.Add(new PermissionOverwrite(adminRoleId.Value, OverwriteTarget.Role, ReadOnly, NoPosting));
            }

            return overwrites;
        }

        // Visible only to the user's role, the admin role (the server owner always sees everything), and the bot.
        // A locked channel stays readable for the user but they cannot post.
        public static IReadOnlyList<PermissionOverwrite> UserChannel(ulong guildId, ulong botUserId, ulong? adminRoleId, ulong userRoleId, bool locked)
        {
            var overwrites = AdminOnly(guildId, botUserId, adminRoleId).ToList();

            overwrites.Add(locked
                ? new PermissionOverwrite(userRoleId, OverwriteTarget.Role, ChannelPermissions.ViewChannel | ChannelPermissions.ReadMessageHistory, ChannelPermissions.SendMessages)
                : new PermissionOverwrite(userRoleId, OverwriteTarget.Role, ReadWrite, ChannelPermissions.None));

            return overwrites;
        }

        public static string UserChannelName(string username) => Truncate(UserChannelPrefix + Slug(username), DiscordLimits.ChannelNameLength);

        public static string UserRoleName(string username) => Truncate(UserRolePrefix + Slug(username), DiscordLimits.RoleNameLength);

        public static string ArchivedName(string channelName) => Truncate(ArchivedPrefix + channelName, DiscordLimits.ChannelNameLength);

        public static string CategoryName(int index) => index <= 1 ? UsersCategoryName : $"{UsersCategoryName} {index}";

        public static string Slug(string username)
        {
            var builder = new StringBuilder();

            foreach (var c in (username ?? string.Empty).ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                }
                else if ((c == '-' || c == '_' || c == '.' || c == ' ') && builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }
            }

            var slug = builder.ToString().Trim('-');

            return slug.Length == 0 ? "user" : slug;
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max).TrimEnd('-');
    }
}
