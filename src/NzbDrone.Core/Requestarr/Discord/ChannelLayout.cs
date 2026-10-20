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
        public const string RequestCategoryName = "Requestarr Requests";
        public const string UserChannelPrefix = "requests-";
        public const string UserRolePrefix = "requestarr-";
        public const string ArchivedPrefix = "archived-";

        private const ChannelPermissions ReadWrite = ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages | ChannelPermissions.ReadMessageHistory;
        private const ChannelPermissions Bot = ReadWrite | ChannelPermissions.EmbedLinks;

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

        public static string CategoryName(int index) => index <= 1 ? RequestCategoryName : $"{RequestCategoryName} {index}";

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
