using System.Linq;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord
{
    // Who is calling and from where, decided by Requestarr on every interaction. Discord
    // command visibility and custom IDs never grant anything.
    public class CallerContext
    {
        public bool RightServer { get; init; }
        public bool IsAdmin { get; init; }
        public bool InAdminChannel { get; init; }
        public bool InFeedChannel { get; init; }
        public RequestarrUser User { get; init; }
        public bool InOwnChannel { get; init; }

        // Text to send back when the caller may not run an admin command here; null when allowed.
        public string AdminDenial => this switch
        {
            { RightServer: false } => DiscordText.WrongServer,
            { IsAdmin: false } => DiscordText.AdminOnly,
            { InAdminChannel: false } => DiscordText.AdminChannelOnly,
            _ => null
        };

        // Buttons on admin cards: the admin channel or the admin feed.
        public string AdminButtonDenial => this switch
        {
            { RightServer: false } => DiscordText.WrongServer,
            { IsAdmin: false } => DiscordText.AdminOnly,
            { InAdminChannel: false, InFeedChannel: false } => DiscordText.AdminChannelOnly,
            _ => null
        };

        // Same for a user command in the caller's own channel.
        public string UserDenial => this switch
        {
            { RightServer: false } => DiscordText.WrongServer,
            { IsAdmin: true, InAdminChannel: true } => DiscordText.AdminUseUserCommand,
            { User: null } or { User.Status: UserStatus.Removed } => DiscordText.NotAUser,
            { User.Status: UserStatus.Suspended } => DiscordText.Suspended,
            { InOwnChannel: false } => DiscordText.OwnChannelOnly,
            _ => null
        };
    }

    public interface ICallerResolver
    {
        CallerContext Resolve(DiscordInteraction interaction);
    }

    public class CallerResolver : ICallerResolver
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestarrUserRepository _users;

        public CallerResolver(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IUserIdentityRepository identities, IRequestarrUserRepository users)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _identities = identities;
            _users = users;
        }

        public CallerContext Resolve(DiscordInteraction interaction)
        {
            if (interaction.GuildId == 0 || interaction.GuildId != _gateway.GuildId)
            {
                return new CallerContext { RightServer = false };
            }

            var adminRoleId = _guildSetup.AdminRoleId;
            var isAdmin = interaction.UserId == _gateway.OwnerId || (adminRoleId.HasValue && interaction.RoleIds.Contains(adminRoleId.Value));
            var config = _guildSetup.GetConfig();

            var identity = _identities.Find(UserIdentity.DiscordProvider, interaction.UserId.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            return new CallerContext
            {
                RightServer = true,
                IsAdmin = isAdmin,
                InAdminChannel = config?.AdminChannelId != null && config.AdminChannelId == interaction.ChannelId.ToString(),
                InFeedChannel = config?.FeedChannelId != null && config.FeedChannelId == interaction.ChannelId.ToString(),
                User = user,
                InOwnChannel = user?.PrivateChannelId != null && user.PrivateChannelId == interaction.ChannelId.ToString()
            };
        }
    }
}
