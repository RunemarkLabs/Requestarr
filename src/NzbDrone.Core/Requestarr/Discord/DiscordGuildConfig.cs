using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Discord
{
    // Discord IDs Requestarr created or adopted in the server. Stored as text (snowflakes).
    public class DiscordGuildConfig : ModelBase
    {
        public string GuildId { get; set; }
        public string AdminRoleId { get; set; }
        public string AdminChannelId { get; set; }
        public string AdminQueueChannelId { get; set; }
        public string AdminPanelMessageId { get; set; }
        public List<string> RequestCategoryIds { get; set; } = new();

        // The admin-only feed: one card per request. Seeded with open requests once per channel.
        public string FeedChannelId { get; set; }
        public bool FeedSeeded { get; set; }

        // Every onboarded member holds the shared members role, which can read (not post in) the
        // announcements channel. AnnouncedVersion: the last version whose changes were posted.
        public string MembersRoleId { get; set; }
        public string AnnouncementsChannelId { get; set; }
        public string AnnouncedVersion { get; set; }
    }

    public interface IDiscordGuildConfigRepository : IBasicRepository<DiscordGuildConfig>
    {
        DiscordGuildConfig FindByGuild(string guildId);
    }

    public class DiscordGuildConfigRepository : BasicRepository<DiscordGuildConfig>, IDiscordGuildConfigRepository
    {
        public DiscordGuildConfigRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public DiscordGuildConfig FindByGuild(string guildId)
        {
            return Query(c => c.GuildId == guildId).SingleOrDefault();
        }
    }
}
