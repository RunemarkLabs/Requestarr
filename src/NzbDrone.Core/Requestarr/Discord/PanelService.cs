using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Access;
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
        public static readonly PanelButton Members = new(Handlers.AdminRoutes.Pick, DiscordText.PanelMembers, ButtonStyle.Success);
        public static readonly PanelButton RemoveTitle = new(Removals.RemovalRoutes.Search, DiscordText.RemoveTitleLabel, ButtonStyle.Danger);

        public static readonly IReadOnlyList<PanelButton> User = new[] { MyRequests, MyLimits, LinkPlex, Help };
        public static readonly IReadOnlyList<PanelButton> Admin = new[] { PendingQueue, Users, Members, RemoveTitle, AdminHelp };

        public static PanelButton ByRoute(string route) => User.Concat(Admin).Single(b => b.Route == route);

        public static InteractionButton ToButton(this PanelButton button) => new() { CustomId = button.Route, Label = button.Label, Style = button.Style };
    }

    public interface IPanelService
    {
        Task EnsureAllAsync();
        Task EnsureAdminPanelAsync();

        // The welcome channel's message and the blocked channel's message.
        Task EnsureWelcomeAsync();

        // Updates the admin panel in place (its Pending queue count); never posts a new one.
        Task RefreshAdminPanelAsync();
        Task RefreshUserPanelAsync(RequestarrUser user);
        InteractionReply UserPanel(RequestarrUser user);
        InteractionReply WelcomePanel();

        // The bot posted something in a channel: its panel moves below it, shortly after (several posts in a row move
        // it once).
        void NoticeBotMessage(ulong channelId, ulong messageId);

        // The channel's panel is its newest message again: re-posted at the bottom, the old one removed.
        Task KeepLastAsync(ulong channelId);
    }

    // One panel per channel that has one, always the newest message so it sits where people look: the admin panel,
    // each member's panel (My requests, My limits, Link Plex while a Plex server is connected and theirs isn't
    // linked), the welcome message and the blocked message. Edited in place; re-posted at the bottom after anything
    // else the bot posts there. Panels aren't pinned: they're always last anyway, and pinning each re-post would add
    // a "pinned a message" notice every time.
    public class PanelService : IPanelService, NzbDrone.Core.Messaging.Events.IHandleAsync<Backends.AdminQueueChangedEvent>
    {
        // How long the panel waits after the bot's last post before moving below it.
        public static readonly TimeSpan MoveDelay = TimeSpan.FromSeconds(2);

        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IMediaServerAccountRepository _accounts;
        private readonly IMediaServerConnections _mediaServers;
        private readonly Backends.IBackendService _backends;
        private readonly Admin.IAdminQueue _queue;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pendingMoves = new();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();

        public PanelService(IDiscordGuildGateway gateway,
                            IGuildSetupService guildSetup,
                            IDiscordGuildConfigRepository configs,
                            IRequestarrUserRepository users,
                            IUserIdentityRepository identities,
                            IUserMediaAccessRepository access,
                            IMediaServerAccountRepository accounts,
                            IMediaServerConnections mediaServers,
                            Backends.IBackendService backends,
                            Admin.IAdminQueue queue,
                            Logger logger)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _configs = configs;
            _users = users;
            _identities = identities;
            _access = access;
            _accounts = accounts;
            _mediaServers = mediaServers;
            _backends = backends;
            _queue = queue;
            _logger = logger;
        }

        public async Task EnsureAllAsync()
        {
            await EnsureAdminPanelAsync();
            await EnsureWelcomeAsync();

            foreach (var user in _users.All().Where(u => u.Status != UserStatus.Removed && u.PrivateChannelId != null))
            {
                await RefreshUserPanelAsync(user);
            }

            // Panels pinned by older versions are unpinned, and every panel ends up last in its channel.
            foreach (var (channel, panel) in Panels().ToList())
            {
                await UnpinAsync(channel, panel);

                try
                {
                    await KeepLastAsync(channel);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not move the panel to the bottom of channel {0}", channel);
                }
            }
        }

        public async Task EnsureAdminPanelAsync()
        {
            var config = _guildSetup.GetConfig();

            if (config?.AdminChannelId == null)
            {
                return;
            }

            var id = await Upsert(ulong.Parse(config.AdminChannelId), config.AdminPanelMessageId, AdminPanel(_queue.Items().Count));

            if (id.HasValue && config.AdminPanelMessageId != id.ToString())
            {
                config.AdminPanelMessageId = id.ToString();
                _configs.Update(config);
            }
        }

        public async Task EnsureWelcomeAsync()
        {
            var config = _guildSetup.GetConfig();

            if (config == null)
            {
                return;
            }

            if (ulong.TryParse(config.WelcomeChannelId, out var welcome))
            {
                config.WelcomeMessageId = (await Upsert(welcome, config.WelcomeMessageId, WelcomePanel()))?.ToString() ?? config.WelcomeMessageId;
            }

            if (ulong.TryParse(config.BlockedChannelId, out var blocked))
            {
                config.BlockedMessageId = (await Upsert(blocked, config.BlockedMessageId, BlockedPanel()))?.ToString() ?? config.BlockedMessageId;
            }

            _configs.Update(config);
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

            // Types with an instance that has connected at least once; others aren't shown to users.
            var allowed = access.Where(a => ContentTypes.IsOffered(a.ContentType) && _backends.EverConnected(a.ContentType) && a.State != AccessState.Off).ToList();

            // Link Plex only while a Plex server is connected and their account isn't linked yet.
            var offerPlex = _mediaServers.IsConnected(MediaServerProvider.Plex) && !_accounts.GetForUser(user.Id).Any();
            var discordId = _identities.GetForUser(user.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;

            var buttons = PanelButtons.User.Where(b => b != PanelButtons.LinkPlex || offerPlex).Select(b => b.ToButton()).ToList();

            return new InteractionReply { Content = DiscordText.UserPanel(discordId, user.DisplayName, allowed), Buttons = buttons };
        }

        // What Requestarr is, Request access, and one button per media server the admin has connected.
        public InteractionReply WelcomePanel()
        {
            var types = ContentTypes.Requestable.Where(_backends.EverConnected).ToList();
            var media = _mediaServers.Connected();
            var buttons = new List<InteractionButton> { new() { CustomId = WelcomeRoutes.Request, Label = DiscordText.RequestAccessLabel, Style = ButtonStyle.Primary } };

            buttons.AddRange(media.Select(p => new InteractionButton { CustomId = $"{WelcomeRoutes.Media}:{p.ToString().ToLowerInvariant()}", Label = DiscordText.MediaAccessLabel(p), Style = ButtonStyle.Secondary }));

            return new InteractionReply { Content = DiscordText.WelcomeMessage(types, media), Buttons = buttons };
        }

        public static InteractionReply BlockedPanel() => new() { Content = DiscordText.BlockedMessage };

        // Pending queue shows how many things wait on the admin ("Pending queue (3)").
        public static InteractionReply AdminPanel(int waiting = 0) => new()
        {
            Content = DiscordText.AdminPanel,
            Buttons = PanelButtons.Admin.Select(b => b == PanelButtons.PendingQueue
                ? new InteractionButton { CustomId = b.Route, Label = DiscordText.PanelPendingQueueCount(waiting), Style = b.Style }
                : b.ToButton()).ToList()
        };

        // Something waiting on the admin changed: the panel's count follows.
        public void HandleAsync(Backends.AdminQueueChangedEvent message) => RefreshAdminPanelAsync().GetAwaiter().GetResult();

        public async Task RefreshAdminPanelAsync()
        {
            var config = _guildSetup.GetConfig();

            if (!ulong.TryParse(config?.AdminChannelId, out var channel) || !ulong.TryParse(config.AdminPanelMessageId, out var panel))
            {
                return;
            }

            try
            {
                await _gateway.EditMessageAsync(channel, panel, AdminPanel(_queue.Items().Count));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not refresh the admin panel");
            }
        }

        public void NoticeBotMessage(ulong channelId, ulong messageId)
        {
            if (PanelIn(channelId) is not { } panel || panel.Id == messageId.ToString())
            {
                return;
            }

            var next = new CancellationTokenSource();

            _pendingMoves.AddOrUpdate(channelId, next, (_, previous) =>
            {
                previous.Cancel();
                return next;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(MoveDelay, next.Token);
                    await KeepLastAsync(channelId);
                }
                catch (OperationCanceledException)
                {
                    // A later post moves it instead.
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not move the panel to the bottom of channel {0}", channelId);
                }
            });
        }

        public async Task KeepLastAsync(ulong channelId)
        {
            var gate = _channelLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            try
            {
                if (PanelIn(channelId) is not { } panel || !ulong.TryParse(panel.Id, out var current) ||
                    await _gateway.GetLatestMessageIdAsync(channelId) == current)
                {
                    return;
                }

                // The new one first, so the channel is never without its panel; it doesn't ping or notify anyone.
                var reply = panel.Build();
                var moved = await _gateway.SendMessageAsync(channelId, new InteractionReply { Content = reply.Content, Embeds = reply.Embeds, Buttons = reply.Buttons, SelectMenu = reply.SelectMenu, Quiet = true });
                panel.Save(moved.ToString());
                await _gateway.DeleteMessageAsync(channelId, current);
            }
            finally
            {
                gate.Release();
            }
        }

        // The panel that lives in a channel: where its ID is kept, and how to build it.
        private record Panel(string Id, Func<InteractionReply> Build, Action<string> Save);

        private Panel PanelIn(ulong channelId)
        {
            var config = _guildSetup.GetConfig();
            var id = channelId.ToString();

            if (config == null)
            {
                return null;
            }

            if (config.AdminChannelId == id)
            {
                return new Panel(config.AdminPanelMessageId, () => AdminPanel(_queue.Items().Count), saved => Save(c => c.AdminPanelMessageId = saved));
            }

            if (config.WelcomeChannelId == id)
            {
                return new Panel(config.WelcomeMessageId, WelcomePanel, saved => Save(c => c.WelcomeMessageId = saved));
            }

            if (config.BlockedChannelId == id)
            {
                return new Panel(config.BlockedMessageId, BlockedPanel, saved => Save(c => c.BlockedMessageId = saved));
            }

            var user = _users.FindByChannel(id);

            return user == null || user.Status == UserStatus.Removed
                ? null
                : new Panel(user.PanelMessageId, () => UserPanel(user), saved =>
                {
                    user.PanelMessageId = saved;
                    _users.Update(user);
                });
        }

        private void Save(Action<DiscordGuildConfig> change)
        {
            var config = _guildSetup.GetConfig();
            change(config);
            _configs.Update(config);
        }

        private IEnumerable<(ulong Channel, ulong Panel)> Panels()
        {
            var config = _guildSetup.GetConfig();
            var pairs = new List<(string, string)>
            {
                (config?.AdminChannelId, config?.AdminPanelMessageId),
                (config?.WelcomeChannelId, config?.WelcomeMessageId),
                (config?.BlockedChannelId, config?.BlockedMessageId)
            };

            pairs.AddRange(_users.All().Where(u => u.Status != UserStatus.Removed).Select(u => (u.PrivateChannelId, u.PanelMessageId)));

            foreach (var (channel, panel) in pairs)
            {
                if (ulong.TryParse(channel, out var c) && ulong.TryParse(panel, out var p))
                {
                    yield return (c, p);
                }
            }
        }

        private async Task<ulong?> Upsert(ulong channelId, string storedId, InteractionReply panel)
        {
            try
            {
                if (!await _gateway.ChannelExistsAsync(channelId))
                {
                    return null;
                }

                return ulong.TryParse(storedId, out var existing) && await _gateway.EditMessageAsync(channelId, existing, panel)
                    ? existing
                    : await _gateway.SendMessageAsync(channelId, panel);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not update the panel in channel {0}", channelId);
                return null;
            }
        }

        private async Task UnpinAsync(ulong channelId, ulong messageId)
        {
            try
            {
                await _gateway.UnpinMessageAsync(channelId, messageId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not unpin the panel in channel {0}", channelId);
            }
        }
    }
}
