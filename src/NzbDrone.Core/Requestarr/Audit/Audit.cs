using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Audit
{
    public class AuditEntry : ModelBase
    {
        public int? ActorUserId { get; set; }
        public string Actor { get; set; }
        public string Action { get; set; }
        public string TargetType { get; set; }
        public string TargetId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Detail { get; set; }
    }

    // Who did something: a Discord member (admin or user), or Requestarr itself.
    public record Actor(string Description, int? UserId = null)
    {
        public static readonly Actor System = new("system");

        public static Actor Discord(ulong discordUserId, string username, int? userId = null) => new($"discord:{discordUserId} ({username})", userId);
    }

    public interface IAuditRepository : IBasicRepository<AuditEntry>
    {
        List<AuditEntry> ForTarget(string targetType, string targetId);
    }

    public class AuditRepository : BasicRepository<AuditEntry>, IAuditRepository
    {
        public AuditRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AuditEntry> ForTarget(string targetType, string targetId)
        {
            return Query(a => a.TargetType == targetType && a.TargetId == targetId).OrderBy(a => a.Id).ToList();
        }
    }

    public interface IAuditService
    {
        void Record(Actor actor, string action, string targetType, string targetId, string detail = null);
    }

    public class AuditService : IAuditService
    {
        private readonly IAuditRepository _repository;

        public AuditService(IAuditRepository repository)
        {
            _repository = repository;
        }

        public void Record(Actor actor, string action, string targetType, string targetId, string detail = null)
        {
            _repository.Insert(new AuditEntry
            {
                ActorUserId = actor.UserId,
                Actor = actor.Description,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Timestamp = DateTime.UtcNow,
                Detail = detail
            });
        }
    }
}
