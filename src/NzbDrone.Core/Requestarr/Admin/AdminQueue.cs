using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Backends;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Admin
{
    // One thing waiting on an admin decision. Key: what it is ("r12" request 12, "t12" request 12
    // in trouble, "p3" Plex link request 3, "i2" instance 2 down, "x4" removal 4). Since: how
    // long it has waited, which orders the walk (oldest first).
    public record QueueItem(string Key, DateTime Since)
    {
        public char Kind => Key[0];

        public int Id => int.Parse(Key.AsSpan(1));

        public long Ticks => Since.Ticks;
    }

    // A kind of thing that can wait on the admin. The walk shows whatever the sources return.
    public interface IAdminQueueSource
    {
        IEnumerable<QueueItem> Items();
    }

    public interface IAdminQueue
    {
        IReadOnlyList<QueueItem> Items();
    }

    public class AdminQueue : IAdminQueue
    {
        private readonly IEnumerable<IAdminQueueSource> _sources;

        public AdminQueue(IEnumerable<IAdminQueueSource> sources)
        {
            _sources = sources;
        }

        public IReadOnlyList<QueueItem> Items() =>
            _sources.SelectMany(s => s.Items()).OrderBy(i => i.Since).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
    }

    // Removals waiting on the admin: a requester's No, a vote kept by keep flags, a deletion Sonarr or
    // Radarr refused (Force or drop each), and an owned removal still waiting on its requester.
    public class RemovalSource : IAdminQueueSource
    {
        private readonly IRemovalProposalRepository _proposals;

        public RemovalSource(IRemovalProposalRepository proposals)
        {
            _proposals = proposals;
        }

        public IEnumerable<QueueItem> Items() =>
            _proposals.WithStatus(RemovalStatus.AwaitingRequester, RemovalStatus.Declined, RemovalStatus.Flagged, RemovalStatus.Failed)
                .Select(p => new QueueItem($"x{p.Id}", p.Status switch
                {
                    RemovalStatus.Declined => p.Answered ?? p.Created,
                    RemovalStatus.Flagged or RemovalStatus.Failed => p.Closed ?? p.Created,
                    _ => p.Created
                }));
    }

    // Requests waiting for approval.
    public class PendingRequestsSource : IAdminQueueSource
    {
        private readonly IRequestRepository _requests;

        public PendingRequestsSource(IRequestRepository requests)
        {
            _requests = requests;
        }

        public IEnumerable<QueueItem> Items() => _requests.WithStatus(RequestStatus.Pending).Select(r => new QueueItem($"r{r.Id}", r.Requested));
    }

    // Approved requests in trouble: the download failed or is stuck, or it couldn't be added at all.
    public class TroubleSource : IAdminQueueSource
    {
        private readonly IRequestRepository _requests;

        public TroubleSource(IRequestRepository requests)
        {
            _requests = requests;
        }

        public static bool InTrouble(Request request) =>
            request.Status == RequestStatus.Failed ||
            (request.Status == RequestStatus.Approved && request.Progress is DownloadProgress.Stalled or DownloadProgress.Failed);

        public IEnumerable<QueueItem> Items() =>
            _requests.WithStatus(RequestStatus.Approved).Concat(_requests.WithStatus(RequestStatus.Failed)).Where(InTrouble)
                .Select(r => new QueueItem($"t{r.Id}", r.ProgressSince ?? r.Decided ?? r.Requested));
    }

    // Plex link requests from members.
    public class PlexLinkSource : IAdminQueueSource
    {
        private readonly IMediaServerAccessRequestRepository _accessRequests;

        public PlexLinkSource(IMediaServerAccessRequestRepository accessRequests)
        {
            _accessRequests = accessRequests;
        }

        public IEnumerable<QueueItem> Items() =>
            _accessRequests.All().Where(r => r.Status == AccessRequestStatus.Pending).Select(r => new QueueItem($"p{r.Id}", r.Requested));
    }

    // Instances down long enough to have alerted the admins, until they're back.
    public class DownInstanceSource : IAdminQueueSource
    {
        private readonly IBackendRepository _backends;

        public DownInstanceSource(IBackendRepository backends)
        {
            _backends = backends;
        }

        public IEnumerable<QueueItem> Items() =>
            _backends.All().Where(b => b.Enable && b.DownSince.HasValue && b.DownAlertMessageId != null).Select(b => new QueueItem($"i{b.Id}", b.DownSince.Value));
    }
}
