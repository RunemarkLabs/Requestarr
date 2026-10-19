using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Users
{
    public interface IRequestarrUserRepository : IBasicRepository<RequestarrUser>
    {
        RequestarrUser FindByChannel(string channelId);
    }

    public class RequestarrUserRepository : BasicRepository<RequestarrUser>, IRequestarrUserRepository
    {
        public RequestarrUserRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public RequestarrUser FindByChannel(string channelId)
        {
            return Query(u => u.PrivateChannelId == channelId).FirstOrDefault();
        }
    }

    public interface IUserIdentityRepository : IBasicRepository<UserIdentity>
    {
        UserIdentity Find(string provider, string externalId);
        List<UserIdentity> GetForUser(int userId);
    }

    public class UserIdentityRepository : BasicRepository<UserIdentity>, IUserIdentityRepository
    {
        public UserIdentityRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public UserIdentity Find(string provider, string externalId)
        {
            return Query(i => i.Provider == provider && i.ExternalId == externalId).SingleOrDefault();
        }

        public List<UserIdentity> GetForUser(int userId)
        {
            return Query(i => i.UserId == userId);
        }
    }

    public interface IUserMediaAccessRepository : IBasicRepository<UserMediaAccess>
    {
        List<UserMediaAccess> GetForUser(int userId);
    }

    public class UserMediaAccessRepository : BasicRepository<UserMediaAccess>, IUserMediaAccessRepository
    {
        public UserMediaAccessRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<UserMediaAccess> GetForUser(int userId)
        {
            return Query(a => a.UserId == userId);
        }
    }
}
