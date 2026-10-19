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
