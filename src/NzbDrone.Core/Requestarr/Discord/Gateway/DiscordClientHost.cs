using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Gateway
{
    public enum DiscordBotState
    {
        NotConfigured,
        Connecting,
        Connected,
        Disconnected,
        Failed
    }

    public class DiscordStatusChangedEvent : IEvent
    {
    }

    public interface IDiscordClientHost
    {
        DiscordSocketClient Client { get; }
        DiscordBotState State { get; }
        string LastError { get; }
        Task StartAsync(DiscordSettings settings);
        Task StopAsync();
        Task WaitUntilReadyAsync(TimeSpan timeout);
    }

    // Owns the Discord.Net gateway connection: login, events, command registration, the
    // watchdog, and handing interactions to the dispatcher.
    public class DiscordClientHost : IDiscordClientHost,
                                     IHandleAsync<ProvisioningCompletedEvent>,
                                     IHandle<ApplicationShutdownRequested>
    {
        // Only what Requestarr needs: server structure and member join/leave.
        public static readonly GatewayIntents Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers;

        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);

        private readonly IDiscordSettingsStore _settings;
        private readonly Lazy<IInteractionDispatcher> _dispatcher;
        private readonly Lazy<IGuildSetupService> _guildSetup;
        private readonly Lazy<IUserManagementService> _users;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly SemaphoreSlim _startLock = new(1, 1);

        private DiscordWatchdog _watchdog;
        private Timer _watchdogTimer;
        private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DiscordClientHost(IDiscordSettingsStore settings,
                                 Lazy<IInteractionDispatcher> dispatcher,
                                 Lazy<IGuildSetupService> guildSetup,
                                 Lazy<IUserManagementService> users,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _settings = settings;
            _dispatcher = dispatcher;
            _guildSetup = guildSetup;
            _users = users;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public DiscordSocketClient Client { get; private set; }
        public DiscordBotState State { get; private set; } = DiscordBotState.NotConfigured;
        public string LastError { get; private set; }

        // Replaceable in tests; the real one ends the process so Docker restarts it.
        public Action<string> Terminate { get; set; } = reason =>
        {
            LogManager.GetCurrentClassLogger().Fatal("Discord watchdog: {0}. Exiting so the container restarts.", reason);
            LogManager.Flush();
            Environment.Exit(1);
        };

        public void HandleAsync(ProvisioningCompletedEvent message)
        {
            var settings = _settings.Current;

            if (!settings.IsConfigured)
            {
                SetState(DiscordBotState.NotConfigured, null);
                return;
            }

            StartAsync(settings).GetAwaiter().GetResult();
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task StartAsync(DiscordSettings settings)
        {
            await _startLock.WaitAsync();

            try
            {
                if (Client != null)
                {
                    return;
                }

                _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                Client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    GatewayIntents = Intents,
                    AlwaysDownloadUsers = false,
                    LogLevel = LogSeverity.Info
                });

                Client.Log += OnLog;
                Client.Connected += OnConnected;
                Client.Disconnected += OnDisconnected;
                Client.LatencyUpdated += (_, _) =>
                {
                    _watchdog?.OnHeartbeat();
                    return Task.CompletedTask;
                };
                Client.Ready += OnReady;
                Client.InteractionCreated += OnInteraction;
                Client.UserLeft += OnUserLeft;

                _watchdog = new DiscordWatchdog(() => DateTime.UtcNow, reason => Terminate(reason));
                _watchdogTimer = new Timer(_ => _watchdog.Check(), null, WatchdogInterval, WatchdogInterval);

                SetState(DiscordBotState.Connecting, null);

                await Client.LoginAsync(TokenType.Bot, settings.BotToken);
                await Client.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Discord bot failed to start");
                SetState(DiscordBotState.Failed, ex.Message);
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async Task StopAsync()
        {
            var client = Client;

            if (client == null)
            {
                return;
            }

            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            Client = null;

            try
            {
                await client.StopAsync();
                await client.LogoutAsync();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error while stopping the Discord client");
            }

            client.Dispose();
        }

        public async Task WaitUntilReadyAsync(TimeSpan timeout)
        {
            if (await Task.WhenAny(_ready.Task, Task.Delay(timeout)) != _ready.Task)
            {
                throw new TimeoutException($"Discord did not become ready within {timeout.TotalSeconds:0} seconds: {LastError}");
            }
        }

        private Task OnConnected()
        {
            _watchdog?.OnConnected();
            SetState(DiscordBotState.Connected, null);
            return Task.CompletedTask;
        }

        private Task OnDisconnected(Exception ex)
        {
            _watchdog?.OnDisconnected();
            SetState(DiscordBotState.Disconnected, ex?.Message);
            return Task.CompletedTask;
        }

        private async Task OnReady()
        {
            try
            {
                var guild = Client.GetGuild(_settings.Current.GuildId);

                if (guild == null)
                {
                    SetState(DiscordBotState.Failed, $"The bot is not a member of server {_settings.Current.GuildId}. Invite it with the link from the setup guide.");
                    return;
                }

                await _guildSetup.Value.EnsureAsync();
                await guild.BulkOverwriteApplicationCommandAsync(DiscordCommandDefinitions.Build().ToArray());

                _logger.Info("Discord bot ready in server '{0}'", guild.Name);
                _ready.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Discord server setup failed");
                SetState(DiscordBotState.Failed, ex.Message);
            }
        }

        private Task OnInteraction(SocketInteraction interaction)
        {
            // Never block the gateway task.
            _ = Task.Run(async () =>
            {
                try
                {
                    var mapped = DiscordInteractionMapper.Map(interaction);

                    if (mapped != null)
                    {
                        await _dispatcher.Value.DispatchAsync(mapped, new DiscordNetResponder(interaction));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Discord interaction failed");
                }
            });

            return Task.CompletedTask;
        }

        private Task OnUserLeft(SocketGuild guild, SocketUser user)
        {
            if (guild.Id != _settings.Current.GuildId || user.IsBot)
            {
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _users.Value.HandleMemberLeftAsync(user.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Handling member departure failed");
                }
            });

            return Task.CompletedTask;
        }

        private Task OnLog(LogMessage message)
        {
            var level = message.Severity switch
            {
                LogSeverity.Critical => LogLevel.Fatal,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warn,
                LogSeverity.Info => LogLevel.Info,
                _ => LogLevel.Debug
            };

            _logger.Log(level, message.Exception, "Discord.Net {0}: {1}", message.Source, message.Message);
            return Task.CompletedTask;
        }

        private void SetState(DiscordBotState state, string error)
        {
            State = state;
            LastError = error;
            _eventAggregator.PublishEvent(new DiscordStatusChangedEvent());
        }
    }

    public static class DiscordInteractionMapper
    {
        public static DiscordInteraction Map(SocketInteraction interaction)
        {
            var member = interaction.User as SocketGuildUser;
            var roles = member?.Roles.Select(r => r.Id).ToList() ?? new List<ulong>();

            DiscordInteraction Base(InteractionKind kind, string command, string customId, Dictionary<string, object> options, IReadOnlyList<string> values) => new()
            {
                Kind = kind,
                CommandName = command,
                CustomId = customId,
                Options = options ?? new Dictionary<string, object>(),
                Values = values ?? new List<string>(),
                UserId = interaction.User.Id,
                Username = interaction.User.Username,
                DisplayName = member?.DisplayName ?? interaction.User.GlobalName ?? interaction.User.Username,
                RoleIds = roles,
                GuildId = interaction.GuildId ?? 0,
                ChannelId = interaction.ChannelId ?? 0
            };

            switch (interaction)
            {
                case SocketSlashCommand command:
                {
                    var path = new List<string> { command.Data.Name };
                    var options = new Dictionary<string, object>();
                    Flatten(command.Data.Options, path, options);
                    return Base(InteractionKind.SlashCommand, string.Join(' ', path), null, options, null);
                }

                case SocketMessageComponent component:
                {
                    var kind = component.Data.Type == ComponentType.Button ? InteractionKind.Button : InteractionKind.SelectMenu;
                    return Base(kind, null, component.Data.CustomId, null, component.Data.Values?.ToList());
                }

                default:
                    return null;
            }
        }

        private static void Flatten(IEnumerable<SocketSlashCommandDataOption> options, List<string> path, Dictionary<string, object> values)
        {
            foreach (var option in options ?? Enumerable.Empty<SocketSlashCommandDataOption>())
            {
                if (option.Type == ApplicationCommandOptionType.SubCommand || option.Type == ApplicationCommandOptionType.SubCommandGroup)
                {
                    path.Add(option.Name);
                    Flatten(option.Options, path, values);
                    continue;
                }

                values[option.Name] = option.Value switch
                {
                    IUser user => user.Id,
                    IRole role => role.Id,
                    IChannel channel => channel.Id,
                    var other => other
                };
            }
        }
    }
}
