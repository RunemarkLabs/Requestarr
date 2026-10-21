using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;
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

    public static class ConnectionRoutes
    {
        public const string Announce = "conn-announce";   // conn-announce:{instanceId}, on the down alert
    }

    public interface IConnectionMonitor
    {
        Task CheckAllAsync();
        Task<bool> CheckAsync(BackendInstance instance);

        // "Announce to users" on a down alert: a plain notice in the announcements channel, and the
        // recovery is then announced there too. The reply is for the admin who pressed it.
        Task<string> AnnounceAsync(int instanceId, string adminName);
    }

    // Tracks never connected / connected / lost per instance. An outage longer than AlertAfter gets
    // one alert in the admin channel and one more when it's back; shorter blips get none. Still down
    // after AnnounceAfter, the alert offers "Announce to users"; only announced outages have their
    // recovery announced.
    public class ConnectionMonitor : IConnectionMonitor, IExecute<ConnectionCheckCommand>, IHandleAsync<BackendsCheckedEvent>
    {
        public static readonly TimeSpan DefaultAlertAfter = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan DefaultAnnounceAfter = TimeSpan.FromMinutes(30);

        private readonly IBackendRepository _repository;
        private readonly IBackendService _backends;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IEventAggregator _events;
        private readonly IAnnouncementService _announcements;
        private readonly Logger _logger;

        public ConnectionMonitor(IBackendRepository repository, IBackendService backends, IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IEventAggregator events, IAnnouncementService announcements, Logger logger)
        {
            _announcements = announcements;
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
        public TimeSpan AnnounceAfter { get; set; } = DefaultAnnounceAfter;

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

            // An admin may have pressed Announce since this instance was loaded.
            instance.DownAnnounceMessageId = _repository.Get(instance.Id).DownAnnounceMessageId;

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

                        // The alert loses its button; an announced outage gets its recovery announced.
                        if (instance.DownAnnounceOffered || instance.DownAnnounceMessageId != null)
                        {
                            await EditAlertAsync(instance, DiscordText.InstanceDownAlert(instance) + "\n" + DiscordText.AlertNowBack(downFor, instance.DownAnnounceMessageId != null), null);
                        }

                        if (instance.DownAnnounceMessageId != null)
                        {
                            await _announcements.PostAsync(DiscordText.OutageOverNotice(TypeOf(instance), downFor));
                        }
                    }

                    instance.DownSince = null;
                    instance.DownAlertMessageId = null;
                    instance.DownAnnounceOffered = false;
                    instance.DownAnnounceMessageId = null;
                    Save(instance, i => i.DownAnnounceMessageId);
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

            if (instance.DownAlertMessageId != null && !instance.DownAnnounceOffered && now - instance.DownSince.Value >= AnnounceAfter && _gateway.IsConnected &&
                await EditAlertAsync(instance, DiscordText.InstanceDownAlert(instance) + "\n" + DiscordText.AnnounceOffer(AnnounceAfter), $"{ConnectionRoutes.Announce}:{instance.Id}"))
            {
                instance.DownAnnounceOffered = true;
                _logger.Info("{0} is still unreachable; the admins can announce it to users", instance.Name);
            }

            Save(instance);
        }

        public async Task<string> AnnounceAsync(int instanceId, string adminName)
        {
            var instance = _repository.All().FirstOrDefault(b => b.Id == instanceId);

            if (instance?.DownSince == null)
            {
                return DiscordText.AnnounceNotDown(instance?.Name);
            }

            if (instance.DownAnnounceMessageId != null)
            {
                return DiscordText.AlreadyAnnounced(instance.Name);
            }

            var id = await _announcements.PostAsync(DiscordText.OutageNotice(TypeOf(instance), PartlyDown(instance)));

            if (!id.HasValue)
            {
                return DiscordText.AnnounceFailed;
            }

            instance.DownAnnounceMessageId = id.ToString();
            _repository.SetFields(instance, i => i.DownAnnounceMessageId);
            await EditAlertAsync(instance, DiscordText.InstanceDownAlert(instance) + "\n" + DiscordText.AlertAnnounced(adminName), null);
            _logger.Info("{0} announced the {1} outage to users", adminName, instance.Name);

            return DiscordText.Announced(instance.Name);
        }

        private static Users.ContentType TypeOf(BackendInstance instance) => DiscordText.TypeOf(instance);

        // Another instance of the same type still answers: only some of that type's requests are paused.
        private bool PartlyDown(BackendInstance instance) =>
            _repository.All().Any(b => b.Id != instance.Id && b.Enable && b.Implementation == instance.Implementation && b.FirstConnected.HasValue && !b.DownSince.HasValue);

        private async Task<bool> EditAlertAsync(BackendInstance instance, string content, string announceButton)
        {
            if (!ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var channel) || !ulong.TryParse(instance.DownAlertMessageId, out var message))
            {
                return false;
            }

            var reply = new InteractionReply
            {
                Content = content,
                Buttons = announceButton == null
                    ? new List<InteractionButton>()
                    : new List<InteractionButton> { new() { CustomId = announceButton, Label = DiscordText.AnnounceLabel, Style = ButtonStyle.Primary } }
            };

            try
            {
                return await _gateway.EditMessageAsync(channel, message, reply);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not update the alert for {0}", instance.Name);
                return false;
            }
        }

        // The announcement itself is written only by AnnounceAsync and cleared on recovery.
        private void Save(BackendInstance instance, params Expression<Func<BackendInstance, object>>[] also) =>
            _repository.SetFields(instance, new Expression<Func<BackendInstance, object>>[] { i => i.FirstConnected, i => i.DownSince, i => i.DownAlertMessageId, i => i.DownAnnounceOffered }.Concat(also).ToArray());

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
