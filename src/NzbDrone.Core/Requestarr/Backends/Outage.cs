using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Backends
{
    // The lasting record of one outage: an instance down long enough to alert the admins (shorter blips aren't
    // outages). Discord's alert and notice are working views that go away on recovery; this stays for the admin web
    // portal's outage history.
    public class Outage : ModelBase
    {
        public int BackendId { get; set; }
        public string InstanceName { get; set; }
        public DateTime Started { get; set; }
        public DateTime? Ended { get; set; }

        // When and by whom it was announced to users; null when it wasn't.
        public DateTime? Announced { get; set; }
        public string AnnouncedBy { get; set; }

        public TimeSpan? Duration => Ended - Started;
    }

    public interface IOutageRepository : IBasicRepository<Outage>
    {
        Outage Ongoing(int backendId);
        List<Outage> History();
    }

    public class OutageRepository : BasicRepository<Outage>, IOutageRepository
    {
        public OutageRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Outage Ongoing(int backendId) => Query(o => o.BackendId == backendId && o.Ended == null).OrderByDescending(o => o.Started).FirstOrDefault();

        public List<Outage> History() => All().OrderByDescending(o => o.Started).ToList();
    }
}
