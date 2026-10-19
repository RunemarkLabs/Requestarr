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
        public RequestarrUser User { get; init; }
        public bool InOwnChannel { get; init; }

        // Text to send back when the caller may not run an admin command here; null when allowed.
        public string AdminDenial =>
            !RightServer ? DiscordText.WrongServer :
            !IsAdmin ? DiscordText.AdminOnly :
            !InAdminChannel ? DiscordText.AdminChannelOnly :
            null;

        // Same for a user command in the caller's own channel.
        public string UserDenial =>
            !RightServer ? DiscordText.WrongServer :
            IsAdmin && InAdminChannel ? DiscordText.AdminUseUserCommand :
            User == null || User.Status == UserStatus.Removed ? DiscordText.NotAUser :
            User.Status == UserStatus.Suspended ? DiscordText.Suspended :
            !InOwnChannel ? DiscordText.OwnChannelOnly :
            null;
    }

    public interface ICallerResolver
    {
        CallerContext Resolve(DiscordInteraction interaction);
    }

    public class CallerResolver : ICallerResolver
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordSettingsStore _settings;
        private readonly IGuildSetupService _guildSetup;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestarrUserRepository _users;

        public CallerResolver(IDiscordGuildGateway gateway, IDiscordSettingsStore settings, IGuildSetupService guildSetup, IUserIdentityRepository identities, IRequestarrUserRepository users)
        {
            _gateway = gateway;
            _settings = settings;
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

            var adminRoleId = _settings.Current.AdminRoleId;
            var isAdmin = interaction.UserId == _gateway.OwnerId || (adminRoleId.HasValue && interaction.RoleIds.Contains(adminRoleId.Value));
            var config = _guildSetup.GetConfig();

            var identity = _identities.Find(UserIdentity.DiscordProvider, interaction.UserId.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            return new CallerContext
            {
                RightServer = true,
                IsAdmin = isAdmin,
                InAdminChannel = config?.AdminChannelId != null && config.AdminChannelId == interaction.ChannelId.ToString(),
                User = user,
                InOwnChannel = user?.PrivateChannelId != null && user.PrivateChannelId == interaction.ChannelId.ToString()
            };
        }
    }
}
