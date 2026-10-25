using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Metadata;

namespace Requestarr.Core.Provisioning
{
    public interface IProvisioningService
    {
        ProvisioningResult LastResult { get; }
        ProvisioningResult Apply();
    }

    // Applies the provisioning file on every start. Keys present in the file win over values
    // changed in the UI; keys left out of the file are not touched.
    public class ProvisioningService : IProvisioningService, IHandle<ApplicationStartedEvent>
    {
        private readonly IProvisioningLoader _loader;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUserService _userService;
        private readonly IDiscordSettingsStore _discordSettings;
        private readonly IBackendService _backends;
        private readonly ITmdbSettingsStore _tmdb;
        private readonly Requestarr.Core.Availability.IAvailabilitySettingsStore _availability;
        private readonly Requestarr.Core.Removals.IRemovalSettings _removal;
        private readonly Requestarr.Core.Access.IAccessSettings _access;
        private readonly Requestarr.Core.Discord.IBotProfileSettings _botProfile;
        private readonly Requestarr.Core.Qa.IBugReportSettings _bugReports;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ProvisioningService(IProvisioningLoader loader,
                                   IConfigFileProvider configFileProvider,
                                   IUserService userService,
                                   IDiscordSettingsStore discordSettings,
                                   IBackendService backends,
                                   ITmdbSettingsStore tmdb,
                                   Requestarr.Core.Availability.IAvailabilitySettingsStore availability,
                                   Requestarr.Core.Removals.IRemovalSettings removal,
                                   Requestarr.Core.Access.IAccessSettings access,
                                   Requestarr.Core.Discord.IBotProfileSettings botProfile,
                                   Requestarr.Core.Qa.IBugReportSettings bugReports,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _loader = loader;
            _configFileProvider = configFileProvider;
            _userService = userService;
            _discordSettings = discordSettings;
            _backends = backends;
            _tmdb = tmdb;
            _availability = availability;
            _removal = removal;
            _access = access;
            _botProfile = botProfile;
            _bugReports = bugReports;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public ProvisioningResult LastResult { get; private set; } = ProvisioningResult.NotRun();

        public void Handle(ApplicationStartedEvent message)
        {
            Apply();
        }

        public ProvisioningResult Apply()
        {
            string path = null;

            try
            {
                path = _loader.ResolvePath();

                if (path == null)
                {
                    _logger.Debug("No provisioning file found, skipping provisioning");
                    LastResult = ProvisioningResult.NoFile();
                }
                else
                {
                    var document = _loader.Load(path);
                    LastResult = Apply(path, document);

                    _logger.Info("Provisioning applied from {0}: {1}", path, string.Join("; ", LastResult.Applied));

                    foreach (var deferred in LastResult.Deferred)
                    {
                        _logger.Info("Provisioning section recognised, applied in a later release: {0}", deferred);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Provisioning failed");
                LastResult = ProvisioningResult.Failed(path, ex.Message);
            }

            _eventAggregator.PublishEvent(new ProvisioningCompletedEvent(LastResult));

            return LastResult;
        }

        private ProvisioningResult Apply(string path, ProvisioningDocument document)
        {
            var applied = new List<string>();
            var deferred = new List<string>();
            var config = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);

            var general = document.General;

            if (general != null)
            {
                AddIfSet(config, "InstanceName", general.InstanceName);
                AddIfSet(config, "LogLevel", general.LogLevel?.ToLowerInvariant());
                AddIfSet(config, "UrlBase", general.UrlBase);
                AddIfSet(config, "Theme", general.Theme);

                if (general.AnalyticsEnabled.HasValue)
                {
                    config["AnalyticsEnabled"] = general.AnalyticsEnabled.Value;
                }

                if (general.LaunchBrowser.HasValue)
                {
                    config["LaunchBrowser"] = general.LaunchBrowser.Value;
                }
            }

            var auth = document.Auth;

            if (auth != null)
            {
                if (auth.Method.IsNotNullOrWhiteSpace())
                {
                    config["AuthenticationMethod"] = Enum.Parse<AuthenticationType>(auth.Method, true);
                }

                if (auth.Required.IsNotNullOrWhiteSpace())
                {
                    config["AuthenticationRequired"] = Enum.Parse<AuthenticationRequiredType>(auth.Required, true);
                }
            }

            if (config.Any())
            {
                _configFileProvider.SaveConfigDictionary(config);
                applied.Add("settings: " + string.Join(", ", config.Keys.OrderBy(k => k)));
            }

            if (auth != null && auth.Username.IsNotNullOrWhiteSpace())
            {
                _userService.Upsert(auth.Username, auth.Password);
                applied.Add("web login user");
            }

            if (auth != null && auth.ApiKey.IsNotNullOrWhiteSpace())
            {
                _configFileProvider.SetApiKey(auth.ApiKey);
                applied.Add("API key");
            }

            var discord = document.Discord;

            _discordSettings.Set(discord == null ? null : new DiscordSettings
            {
                BotToken = discord.BotToken,
                ApplicationId = ParseId(discord.ApplicationId) ?? 0,
                GuildId = ParseId(discord.GuildId) ?? 0,
                AdminRoleId = ParseId(discord.AdminRoleId),
                AdminRoleName = string.IsNullOrWhiteSpace(discord.AdminRoleName) ? DiscordSettings.DefaultAdminRoleName : discord.AdminRoleName.Trim(),
                AdminChannelId = ParseId(discord.AdminChannelId),
                RequestCategoryId = ParseId(discord.RequestCategoryIds?.FirstOrDefault())
            });

            if (discord != null)
            {
                applied.Add(_discordSettings.Current.IsConfigured ? "discord bot" : "discord (no bot token, bot not started)");
            }

            _tmdb.Set(document.Tmdb?.ApiKey);

            if (_tmdb.Enabled)
            {
                applied.Add("The Movie Database key (cast enabled)");
            }

            _availability.Set(document.Availability?.WebhookUrl, document.Availability?.PollMinutes);
            applied.Add(_availability.WebhookUrl != null
                ? $"availability (webhooks to {_availability.WebhookUrl}, backup poll every {_availability.PollMinutes} min)"
                : $"availability (polling every {_availability.PollMinutes} min; no webhookUrl)");

            _removal.Apply(document.Removal);
            applied.Add($"removal votes ({(_removal.FastVotes ? "DEV FAST VOTES ON (minutes); settings: " : string.Empty)}{_removal.VoteDays} days; requester answers within {_removal.RequesterTimeoutDays} days{(_removal.ReminderDay > 0 ? $", reminded on day {_removal.ReminderDay}" : string.Empty)})");

            _access.Apply(document.AccessRequests);
            applied.Add($"access requests (ask again {_access.DenialCooldownDays} days after a denial; spam guard: {_access.ShortCooldownClicks} clicks in {_access.SpamWindowMinutes} min wait {_access.ShortCooldownMinutes} min, " +
                        $"{_access.LongCooldownClicks} wait {_access.LongCooldownHours} h, {_access.BlockClicks} more within {_access.BlockWithinHours} h block)");

            _botProfile.Apply(document.BotProfile);
            applied.Add(_botProfile.KeepUpToDate
                ? $"bot picture and banner kept up to date{(_botProfile.TakeOver ? " (replacing ones chosen by hand)" : string.Empty)}"
                : "bot picture and banner left as they are");

            _bugReports.Apply(document.BugReports);
            applied.Add(!_bugReports.FileIssues
                ? $"bug reports (posted and kept; not filed on GitHub; {_bugReports.ReportsPerDay} a day, {_bugReports.MinutesBetween} min apart per person)"
                : $"bug reports (filed on GitHub in {_bugReports.Repository}{(_bugReports.Token == null ? ", but there's no githubToken, so none are filed" : string.Empty)}; {_bugReports.ReportsPerDay} a day, {_bugReports.MinutesBetween} min apart per person)");

            _backends.SyncFromProvisioning(document.Backends);

            if (document.Backends != null)
            {
                var counts = new (string Type, int Count)[]
                    {
                        ("sonarr", document.Backends.Sonarr.Count),
                        ("radarr", document.Backends.Radarr.Count)
                    }
                    .Where(c => c.Count > 0)
                    .Select(c => $"{c.Type} x{c.Count}")
                    .ToList();

                if (counts.Any())
                {
                    applied.Add("backends (" + string.Join(", ", counts) + ")");
                }

                if (document.Backends.Lidarr.Any())
                {
                    deferred.Add($"backends (lidarr x{document.Backends.Lidarr.Count})");
                }
            }

            if (document.MediaServers != null)
            {
                deferred.Add("mediaServers");
            }

            return ProvisioningResult.Succeeded(path, applied, deferred);
        }

        private static ulong? ParseId(string value) => ulong.TryParse(value, out var id) ? id : null;

        private static void AddIfSet(Dictionary<string, object> config, string key, string value)
        {
            if (value != null)
            {
                config[key] = value;
            }
        }
    }
}
