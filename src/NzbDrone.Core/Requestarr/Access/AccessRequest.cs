using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Access
{
    public enum AccessRequestState
    {
        Pending = 0,
        Approved = 1,
        Denied = 2
    }

    // Someone in the server asking for Requestarr from the welcome channel: the content types they'd like and an
    // optional note. Nobody is onboarded by asking; the admin approves by choosing the real access levels.
    public class AccessRequest : ModelBase
    {
        public string DiscordUserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public List<ContentType> ContentTypes { get; set; } = new();
        public string Note { get; set; }

        // Media server access asked for in the same form; it goes to the admin as its own link request.
        public MediaServerProvider? MediaProvider { get; set; }
        public string MediaUsername { get; set; }

        public AccessRequestState Status { get; set; }
        public DateTime Requested { get; set; }
        public DateTime? Decided { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }

        // Why it was denied, if the admin said.
        public string Reason { get; set; }

        // The card in the admin channel.
        public string CardMessageId { get; set; }
    }

    public interface IAccessRequestRepository : IBasicRepository<AccessRequest>
    {
        List<AccessRequest> Pending();
        List<AccessRequest> ForMember(string discordUserId);
    }

    public class AccessRequestRepository : BasicRepository<AccessRequest>, IAccessRequestRepository
    {
        public AccessRequestRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AccessRequest> Pending() => Query(r => r.Status == AccessRequestState.Pending);

        public List<AccessRequest> ForMember(string discordUserId) => Query(r => r.DiscordUserId == discordUserId).OrderBy(r => r.Requested).ToList();
    }
}
