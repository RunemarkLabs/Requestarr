using System.Collections.Generic;

namespace Requestarr.Core.Provisioning
{
    // Shape of the startup provisioning file (YAML, camelCase keys).
    // Sections marked Phase 1 are parsed and validated now and applied once the matching feature exists.
    public class ProvisioningDocument
    {
        public int Version { get; set; } = 1;
        public GeneralProvisioning General { get; set; }
        public AuthProvisioning Auth { get; set; }

        // Phase 1
        public DiscordProvisioning Discord { get; set; }
        public BackendsProvisioning Backends { get; set; }
        public MediaServersProvisioning MediaServers { get; set; }

        // Optional: cast and episode counts from The Movie Database.
        public TmdbProvisioning Tmdb { get; set; }

        // Optional: how Sonarr and Radarr tell Requestarr about downloads (webhook plus a backup poll).
        public AvailabilityProvisioning Availability { get; set; }

        // Optional: removal vote timings (days). Stored as settings the admin portal can change.
        public RemovalProvisioning Removal { get; set; }

        // Optional: access requests from the welcome channel. Stored as settings the admin portal can change.
        public AccessRequestsProvisioning AccessRequests { get; set; }

        // Optional: the bot's own Discord picture and banner. Stored as settings the admin portal can change.
        public BotProfileProvisioning BotProfile { get; set; }
    }

    public class BotProfileProvisioning
    {
        // Requestarr sets the bot's picture and banner and updates them when a release changes them (default true).
        public bool? KeepUpToDate { get; set; }

        // Replace a picture or banner someone else chose too, instead of leaving it (default false).
        public bool? TakeOver { get; set; }
    }

    public class AccessRequestsProvisioning
    {
        // Days someone waits after a denial before they can ask again (default 7; 0 lets them ask right away).
        public int? DenialCooldownDays { get; set; }

        public AccessSpamProvisioning Spam { get; set; }
    }

    // Clicks on the welcome buttons that can't do anything (a request already open, a cooldown, the status again).
    public class AccessSpamProvisioning
    {
        // Only clicks within this many minutes count (default 60).
        public int? WindowMinutes { get; set; }

        // This many counted clicks: a short cooldown (default 5 clicks, 1 minute).
        public int? ShortCooldownClicks { get; set; }
        public int? ShortCooldownMinutes { get; set; }

        // This many: a long cooldown (default 10 clicks, 24 hours).
        public int? LongCooldownClicks { get; set; }
        public int? LongCooldownHours { get; set; }

        // This many more within so many hours of the long cooldown: blocked until the admin lifts it (default 5, 24).
        public int? BlockClicks { get; set; }
        public int? BlockWithinHours { get; set; }
    }

    public class AvailabilityProvisioning
    {
        // The address Sonarr and Radarr use to reach Requestarr, with any URL base, for example
        // http://requestarr:9797 on the same Docker network. Left out: polling only.
        public string WebhookUrl { get; set; }

        // Minutes between backup polls (default 15, at least 5).
        public int? PollMinutes { get; set; }
    }

    public class RemovalProvisioning
    {
        // How long a removal vote stays open (default 7).
        public int? VoteDays { get; set; }

        // How long the requester has to answer an admin's removal before silence counts as yes (default 7).
        public int? RequesterTimeoutDays { get; set; }

        // Which day of that the requester is reminded (default 5).
        public int? ReminderDay { get; set; }
    }

    public class TmdbProvisioning
    {
        // A v4 API Read Access Token or a v3 API key; empty disables cast.
        public string ApiKey { get; set; }
    }

    public class GeneralProvisioning
    {
        public string InstanceName { get; set; }
        public string LogLevel { get; set; }
        public string UrlBase { get; set; }
        public bool? AnalyticsEnabled { get; set; }
        public bool? LaunchBrowser { get; set; }
        public string Theme { get; set; }
    }

    public class AuthProvisioning
    {
        // none | basic | forms | external
        public string Method { get; set; }

        // enabled | disabledForLocalAddresses
        public string Required { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string ApiKey { get; set; }
    }

    public class DiscordProvisioning
    {
        public string BotToken { get; set; }
        public string ApplicationId { get; set; }
        public string GuildId { get; set; }
        public string AdminRoleId { get; set; }

        // Role created on first connect when no adminRoleId is given; members holding it are admins.
        public string AdminRoleName { get; set; }
        public string AdminChannelId { get; set; }
        public string AdminQueueChannelId { get; set; }
        public List<string> RequestCategoryIds { get; set; } = new();
    }

    // Each backend type takes a list, so any number of instances per type.
    public class BackendsProvisioning
    {
        public List<BackendInstanceProvisioning> Sonarr { get; set; } = new();
        public List<BackendInstanceProvisioning> Radarr { get; set; } = new();
        public List<BackendInstanceProvisioning> Lidarr { get; set; } = new();
    }

    public class BackendInstanceProvisioning
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public bool Enabled { get; set; } = true;

        // The instance used for users without an explicit assignment; the first listed when none is marked.
        public bool Default { get; set; }
        public bool PollingOnly { get; set; }
        public string QualityProfile { get; set; }
        public string RootFolder { get; set; }

        // Sonarr only: standard | anime | daily
        public string SeriesType { get; set; }
        public List<string> Tags { get; set; } = new();

        // Sonarr only: anime sub/dub. Optional; on with the defaults when left out.
        public AnimeProvisioning Anime { get; set; }
    }

    public class AnimeProvisioning
    {
        // false: sub/dub is not offered on this instance (anime is still added as Anime type).
        public bool? Enabled { get; set; }

        // dub (default) | sub | first
        public string Priority { get; set; }
        public AnimeTermsProvisioning Dub { get; set; }
        public AnimeTermsProvisioning Sub { get; set; }
    }

    // Release terms for one language's Sonarr release profile. Leave a list out to keep the default.
    public class AnimeTermsProvisioning
    {
        public List<string> Required { get; set; }
        public List<string> Ignored { get; set; }
    }

    public class MediaServersProvisioning
    {
        public List<MediaServerProvisioning> Plex { get; set; } = new();
        public List<MediaServerProvisioning> Jellyfin { get; set; } = new();
    }

    public class MediaServerProvisioning
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Token { get; set; }
    }
}
