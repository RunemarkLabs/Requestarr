using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Users;

namespace Requestarr.Core.Backends
{
    public static class BackendImplementations
    {
        public const string Sonarr = "Sonarr";
        public const string Radarr = "Radarr";
        public const string Lidarr = "Lidarr";

        public static string ForContentType(ContentType type) => type switch
        {
            ContentType.Movie => Radarr,
            ContentType.Tv => Sonarr,
            ContentType.Music => Lidarr,
            _ => null
        };
    }

    // One configured Sonarr / Radarr / Lidarr instance (row in Backends). Any number per type.
    public class BackendInstance : ModelBase
    {
        public string Name { get; set; }
        public string Implementation { get; set; }
        public BackendSettings Settings { get; set; } = new();
        public string ConfigContract { get; set; } = nameof(BackendSettings);
        public bool Enable { get; set; } = true;
        public List<int> Tags { get; set; } = new();
        public DateTime Added { get; set; }

        // Connection state. Never connected (FirstConnected empty): users don't see its content type.
        // Lost (DownSince set): users still see the type; its requests say they're paused.
        public DateTime? FirstConnected { get; set; }
        public DateTime? DownSince { get; set; }
        public string DownAlertMessageId { get; set; }

        // This outage: the alert got its "Announce to users" button; the announcement, if an admin pressed it.
        public bool DownAnnounceOffered { get; set; }
        public string DownAnnounceMessageId { get; set; }

        public bool EverConnected => FirstConnected.HasValue;
    }

    public class BackendSettings : IEmbeddedDocument
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string QualityProfile { get; set; }
        public string RootFolder { get; set; }
        public string SeriesType { get; set; }
        public bool IsDefault { get; set; }
        public bool PollingOnly { get; set; }

        // Sonarr only: anime sub/dub (see AnimeDefaults). On unless the instance opts out.
        public bool AnimeEnabled { get; set; } = true;
        public string AnimePriority { get; set; } = "dub";
        public List<string> DubRequired { get; set; }
        public List<string> DubIgnored { get; set; }
        public List<string> SubRequired { get; set; }
        public List<string> SubIgnored { get; set; }
    }

    public interface IBackendRepository : IBasicRepository<BackendInstance>
    {
        BackendInstance FindByName(string name);
    }

    public class BackendRepository : BasicRepository<BackendInstance>, IBackendRepository
    {
        public BackendRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public BackendInstance FindByName(string name)
        {
            return All().FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
