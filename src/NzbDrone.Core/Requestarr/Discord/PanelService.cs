using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord
{
    public static class PanelRoutes
    {
        public const string MyRequests = "panel-requests";
        public const string MyLimits = "panel-limits";
        public const string LinkPlex = "panel-plex";
        public const string LinkPlexForm = "panel-plex-form";
        public const string PlexUsernameField = "username";
        public const string Help = "panel-help";
        public const string AdminQueue = "adminpanel-queue";
        public const string AdminUsers = "adminpanel-users";
        public const string AdminHelp = "adminpanel-help";
    }

    public record PanelButton(string Route, string Label, ButtonStyle Style);

    // Every panel button, once: the panels and the help topics are both built from these.
    public static class PanelButtons
    {
        public static readonly PanelButton MyRequests = new(PanelRoutes.MyRequests, DiscordText.PanelMyRequests, ButtonStyle.Primary);
        public static readonly PanelButton MyLimits = new(PanelRoutes.MyLimits, DiscordText.PanelMyLimits, ButtonStyle.Secondary);
        public static readonly PanelButton LinkPlex = new(PanelRoutes.LinkPlex, DiscordText.PanelLinkPlex, ButtonStyle.Success);
        public static readonly PanelButton Help = new(PanelRoutes.Help, DiscordText.PanelHelp, ButtonStyle.Secondary);
        public static readonly PanelButton PendingQueue = new(PanelRoutes.AdminQueue, DiscordText.PanelPendingQueue, ButtonStyle.Primary);
        public static readonly PanelButton Users = new(PanelRoutes.AdminUsers, DiscordText.PanelUsers, ButtonStyle.Secondary);
        public static readonly PanelButton AdminHelp = new(PanelRoutes.AdminHelp, DiscordText.PanelHelp, ButtonStyle.Secondary);

        public static readonly IReadOnlyList<PanelButton> User = new[] { MyRequests, MyLimits, LinkPlex, Help };
        public static readonly IReadOnlyList<PanelButton> Admin = new[] { PendingQueue, Users, AdminHelp };

        public static PanelButton ByRoute(string route) => User.Concat(Admin).Single(b => b.Route == route);

        public static InteractionButton ToButton(this PanelButton button) => new() { CustomId = button.Route, Label = button.Label, Style = button.Style };
    }

    public interface IPanelService
    {
        Task EnsureAllAsync();
        Task EnsureAdminPanelAsync();
        Task RefreshUserPanelAsync(RequestarrUser user);
        InteractionReply UserPanel(RequestarrUser user);
    }

    // One pinned panel per user channel (My requests, My limits, Link Plex until linked) and one
    // in the admin channel (Pending queue, Users). Edited in place; re-posted if someone deleted it.
    public class PanelService : IPanelService
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly Logger _logger;

        public PanelService(IDiscordGuildGateway gateway,
                            IGuildSetupService guildSetup,
                            IDiscordGuildConfigRepository configs,
                            IRequestarrUserRepository users,
                            IUserIdentityRepository identities,
                            IUserMediaAccessRepository access,
                            IMediaServerAccountRepository accounts,
                            Logger logger)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _configs = configs;
            _users = users;
            _identities = identities;
            _access = access;
            _accounts = accounts;
            _logger = logger;
        }

        public async Task EnsureAllAsync()
        {
            await EnsureAdminPanelAsync();

            foreach (var user in _users.All().Where(u => u.Status != UserStatus.Removed && u.PrivateChannelId != null))
            {
                await RefreshUserPanelAsync(user);
            }
        }

        public async Task EnsureAdminPanelAsync()
        {
            var config = _guildSetup.GetConfig();

            if (config?.AdminChannelId == null)
            {
                return;
            }

            var id = await Upsert(ulong.Parse(config.AdminChannelId), config.AdminPanelMessageId, AdminPanel());

            if (id.HasValue && config.AdminPanelMessageId != id.ToString())
            {
                config.AdminPanelMessageId = id.ToString();
                _configs.Update(config);
            }
        }

        public async Task RefreshUserPanelAsync(RequestarrUser user)
        {
            if (user?.PrivateChannelId == null || user.Status == UserStatus.Removed)
            {
                return;
            }

            var id = await Upsert(ulong.Parse(user.PrivateChannelId), user.PanelMessageId, UserPanel(user));

            if (id.HasValue && user.PanelMessageId != id.ToString())
            {
                user.PanelMessageId = id.ToString();
                _users.Update(user);
            }
        }

        public InteractionReply UserPanel(RequestarrUser user)
        {
            var access = _access.GetForUser(user.Id);
            var allowed = access.Where(a => ContentTypes.Requestable.Contains(a.ContentType) && a.State != AccessState.Off).ToList();
            var linked = _accounts.GetForUser(user.Id).Any();
            var discordId = _identities.GetForUser(user.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;

            var buttons = PanelButtons.User.Where(b => b != PanelButtons.LinkPlex || !linked).Select(b => b.ToButton()).ToList();

            return new InteractionReply { Content = DiscordText.UserPanel(discordId, user.DisplayName, allowed), Buttons = buttons };
        }

        public static InteractionReply AdminPanel() => new()
        {
            Content = DiscordText.AdminPanel,
            Buttons = PanelButtons.Admin.Select(b => b.ToButton()).ToList()
        };

        private async Task<ulong?> Upsert(ulong channelId, string storedId, InteractionReply panel)
        {
            try
            {
                if (!await _gateway.ChannelExistsAsync(channelId))
                {
                    return null;
                }

                var id = ulong.TryParse(storedId, out var existing) && await _gateway.EditMessageAsync(channelId, existing, panel)
                    ? existing
                    : await _gateway.SendMessageAsync(channelId, panel);

                await Pin(channelId, id);
                return id;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not update the panel in channel {0}", channelId);
                return null;
            }
        }

        // Pinning needs the Pin Messages permission. Without it the panel still works, it just isn't pinned.
        private async Task Pin(ulong channelId, ulong messageId)
        {
            try
            {
                await _gateway.PinMessageAsync(channelId, messageId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not pin the panel in channel {0}. Give the bot the Pin Messages permission.", channelId);
            }
        }
    }
}
