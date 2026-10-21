using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Backends
{
    // Every minute: is each Sonarr / Radarr reachable?
    public class ConnectionCheckCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }

    // An instance came back after an outage. WasAlerted: the admins had been told it was down.
    public class InstanceRecoveredEvent : IEvent
    {
        public InstanceRecoveredEvent(BackendInstance instance, TimeSpan downFor, bool wasAlerted)
        {
            Instance = instance;
            DownFor = downFor;
            WasAlerted = wasAlerted;
        }

        public BackendInstance Instance { get; }
        public TimeSpan DownFor { get; }
        public bool WasAlerted { get; }
    }

    // Something that waits on the admin changed (a request, a Plex link, an instance down or back).
    public class AdminQueueChangedEvent : IEvent
    {
    }

    public interface IConnectionMonitor
    {
        Task CheckAllAsync();
        Task<bool> CheckAsync(BackendInstance instance);
    }

    // Tracks never connected / connected / lost per instance. An outage longer than AlertAfter gets
    // one alert in the admin channel and one more when it's back; shorter blips get none.
    public class ConnectionMonitor : IConnectionMonitor, IExecute<ConnectionCheckCommand>, IHandleAsync<BackendsCheckedEvent>
    {
        public static readonly TimeSpan DefaultAlertAfter = TimeSpan.FromMinutes(3);

        private readonly IBackendRepository _repository;
        private readonly IBackendService _backends;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public ConnectionMonitor(IBackendRepository repository, IBackendService backends, IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IEventAggregator events, Logger logger)
        {
            _repository = repository;
            _backends = backends;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _events = events;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        // Settable so the live check can use a shorter outage instead of waiting minutes.
        public TimeSpan AlertAfter { get; set; } = DefaultAlertAfter;

        public void Execute(ConnectionCheckCommand message) => CheckAllAsync().GetAwaiter().GetResult();

        public void HandleAsync(BackendsCheckedEvent message) => CheckAllAsync().GetAwaiter().GetResult();

        public async Task CheckAllAsync()
        {
            foreach (var instance in _repository.All().Where(b => b.Enable && b.Implementation is BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                await CheckAsync(instance);
            }
        }

        public async Task<bool> CheckAsync(BackendInstance instance)
        {
            bool reachable;

            try
            {
                var status = await _backends.ApiFor(instance).GetStatusAsync(instance.Settings);
                reachable = string.Equals(status.AppName, instance.Implementation, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException)
            {
                reachable = false;
            }

            await ApplyAsync(instance, reachable);
            return reachable;
        }

        private async Task ApplyAsync(BackendInstance instance, bool reachable)
        {
            var now = Clock();

            if (reachable)
            {
                instance.FirstConnected ??= now;

                if (instance.DownSince.HasValue)
                {
                    var downFor = now - instance.DownSince.Value;
                    var alerted = instance.DownAlertMessageId != null;

                    if (alerted)
                    {
                        await PostAsync(new InteractionReply { Content = DiscordText.InstanceBackAlert(instance, downFor) });
                        _logger.Info("{0} is reachable again after {1:N0} minutes", instance.Name, downFor.TotalMinutes);
                    }

                    instance.DownSince = null;
                    instance.DownAlertMessageId = null;
                    Save(instance);
                    _events.PublishEvent(new InstanceRecoveredEvent(instance, downFor, alerted));

                    if (alerted)
                    {
                        _events.PublishEvent(new AdminQueueChangedEvent());
                    }

                    return;
                }

                Save(instance);
                return;
            }

            // Never connected: nothing was lost, and its type is hidden from users anyway.
            if (!instance.FirstConnected.HasValue)
            {
                return;
            }

            instance.DownSince ??= now;

            if (instance.DownAlertMessageId == null && now - instance.DownSince.Value >= AlertAfter && _gateway.IsConnected)
            {
                var id = await PostAsync(new InteractionReply { Content = DiscordText.InstanceDownAlert(instance) });

                if (id.HasValue)
                {
                    instance.DownAlertMessageId = id.ToString();
                    _logger.Warn("{0} has been unreachable since {1:u}; the admins were alerted", instance.Name, instance.DownSince);
                    Save(instance);
                    _events.PublishEvent(new AdminQueueChangedEvent());
                    return;
                }
            }

            Save(instance);
        }

        private void Save(BackendInstance instance) => _repository.SetFields(instance, i => i.FirstConnected, i => i.DownSince, i => i.DownAlertMessageId);

        private async Task<ulong?> PostAsync(InteractionReply message)
        {
            if (!ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var channel))
            {
                return null;
            }

            try
            {
                return await _gateway.SendMessageAsync(channel, message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post a connection alert");
                return null;
            }
        }
    }
}
