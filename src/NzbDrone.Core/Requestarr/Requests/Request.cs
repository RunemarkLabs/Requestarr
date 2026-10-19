using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    public enum RequestStatus
    {
        Pending = 0,
        Approved = 1,
        Denied = 2,
        Cancelled = 3,
        Failed = 4,
        Available = 5
    }

    public class Request : ModelBase
    {
        public int OwnerUserId { get; set; }
        public ContentType ContentType { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string MusicBrainzId { get; set; }
        public string ComicVineId { get; set; }
        public string Isbn { get; set; }
        public RequestStatus Status { get; set; }
        public int? BackendId { get; set; }
        public int? QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }
        public List<int> Seasons { get; set; } = new();
        public bool MonitorFutureSeasons { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecisionReason { get; set; }
        public int? ExternalItemId { get; set; }
        public DateTime Requested { get; set; }
        public DateTime? Decided { get; set; }
        public DateTime? Fulfilled { get; set; }

        public int? ExternalId => ContentType == ContentType.Tv ? TvdbId : TmdbId;

        public bool IsActive => Status == RequestStatus.Pending || Status == RequestStatus.Approved || Status == RequestStatus.Available;

        public string Label => Year.HasValue ? $"{Title} ({Year})" : Title;
    }

    public class RequestSubscriber : ModelBase
    {
        public int RequestId { get; set; }
        public int UserId { get; set; }
        public DateTime Added { get; set; }
    }

    public interface IRequestRepository : IBasicRepository<Request>
    {
        List<Request> ForTitle(ContentType type, int externalId);
        List<Request> OwnedBy(int userId);
        List<Request> WithStatus(RequestStatus status);
    }

    public class RequestRepository : BasicRepository<Request>, IRequestRepository
    {
        public RequestRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Request> ForTitle(ContentType type, int externalId)
        {
            return type == ContentType.Tv
                ? Query(r => r.ContentType == type && r.TvdbId == externalId)
                : Query(r => r.ContentType == type && r.TmdbId == externalId);
        }

        public List<Request> OwnedBy(int userId) => Query(r => r.OwnerUserId == userId);

        public List<Request> WithStatus(RequestStatus status) => Query(r => r.Status == status);
    }

    public interface IRequestSubscriberRepository : IBasicRepository<RequestSubscriber>
    {
        List<RequestSubscriber> ForRequest(int requestId);
        List<RequestSubscriber> ForUser(int userId);
    }

    public class RequestSubscriberRepository : BasicRepository<RequestSubscriber>, IRequestSubscriberRepository
    {
        public RequestSubscriberRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<RequestSubscriber> ForRequest(int requestId) => Query(s => s.RequestId == requestId);

        public List<RequestSubscriber> ForUser(int userId) => Query(s => s.UserId == userId);
    }

    public static class RequestOrdering
    {
        public static IEnumerable<Request> Newest(this IEnumerable<Request> requests) => requests.OrderByDescending(r => r.Requested).ThenByDescending(r => r.Id);
    }
}
