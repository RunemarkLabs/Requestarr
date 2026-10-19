using System;
using NzbDrone.Core.Datastore;

namespace Requestarr.Core.Users
{
    // A person who may request media. Not the web-portal login (Servarr's Users table).
    public class RequestarrUser : ModelBase
    {
        public string DisplayName { get; set; }
        public UserStatus Status { get; set; }
        public string DiscordRoleId { get; set; }
        public string PrivateChannelId { get; set; }
        public DateTime Added { get; set; }
        public DateTime? StatusChanged { get; set; }
    }

    public class UserIdentity : ModelBase
    {
        public const string DiscordProvider = "Discord";

        public int UserId { get; set; }
        public string Provider { get; set; }
        public string ExternalId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public DateTime Linked { get; set; }
    }

    public class UserMediaAccess : ModelBase
    {
        public int UserId { get; set; }
        public ContentType ContentType { get; set; }
        public AccessState State { get; set; }
        public int? BackendId { get; set; }
        public int? QuotaLimit { get; set; }

        // TimeSpan as stored by Servarr's TimeSpanConverter; null means no quota window.
        public TimeSpan? QuotaWindow { get; set; }
    }
}
