using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.MediaServers
{
    public enum MediaServerProvider
    {
        Plex = 1,
        Jellyfin = 2
    }

    public enum ShareStatus
    {
        None = 0,
        Requested = 1,
        Invited = 2,
        Active = 3,
        Revoked = 4
    }

    public enum AccessRequestStatus
    {
        Pending = 0,
        Approved = 1,
        Denied = 2
    }

    public class MediaServerAccount : ModelBase
    {
        public int UserId { get; set; }
        public MediaServerProvider Provider { get; set; }
        public string Username { get; set; }
        public ShareStatus ShareStatus { get; set; }
        public DateTime Added { get; set; }
    }

    public class MediaServerAccessRequest : ModelBase
    {
        public int UserId { get; set; }
        public MediaServerProvider Provider { get; set; }
        public string Username { get; set; }
        public AccessRequestStatus Status { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTime Requested { get; set; }
        public DateTime? Decided { get; set; }
    }

    public interface IMediaServerAccountRepository : IBasicRepository<MediaServerAccount>
    {
        MediaServerAccount Find(int userId, MediaServerProvider provider);
        List<MediaServerAccount> GetForUser(int userId);
    }

    public class MediaServerAccountRepository : BasicRepository<MediaServerAccount>, IMediaServerAccountRepository
    {
        public MediaServerAccountRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public MediaServerAccount Find(int userId, MediaServerProvider provider)
        {
            return Query(a => a.UserId == userId && a.Provider == provider).SingleOrDefault();
        }

        public List<MediaServerAccount> GetForUser(int userId)
        {
            return Query(a => a.UserId == userId);
        }
    }

    public interface IMediaServerAccessRequestRepository : IBasicRepository<MediaServerAccessRequest>
    {
        List<MediaServerAccessRequest> GetPendingForUser(int userId);
    }

    public class MediaServerAccessRequestRepository : BasicRepository<MediaServerAccessRequest>, IMediaServerAccessRequestRepository
    {
        public MediaServerAccessRequestRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MediaServerAccessRequest> GetPendingForUser(int userId)
        {
            return Query(r => r.UserId == userId && r.Status == AccessRequestStatus.Pending);
        }
    }
}
