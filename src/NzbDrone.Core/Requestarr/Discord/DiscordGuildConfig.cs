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

        // The Requestarr Admin group (admin channel and feed) and the Requestarr Users groups (welcome,
        // announcements and blocked channels in the first, then every member's private channel, spilling
        // into "Requestarr Users 2", "3" and on). The users groups were the request groups before task 4e.
        public string AdminCategoryId { get; set; }
        public List<string> RequestCategoryIds { get; set; } = new();

        // The welcome channel everyone can read, and its one message with the access request buttons.
        public string WelcomeChannelId { get; set; }
        public string WelcomeMessageId { get; set; }

        // Someone who keeps clicking the welcome buttons gets the blocked role: it hides the welcome channel and
        // shows the blocked channel, with its one message.
        public string BlockedRoleId { get; set; }
        public string BlockedChannelId { get; set; }
        public string BlockedMessageId { get; set; }

        // The admin-only feed: one card per request. Seeded with open requests once per channel.
        public string FeedChannelId { get; set; }
        public bool FeedSeeded { get; set; }

        // Every onboarded member holds the shared members role, which can read (not post in) the
        // announcements channel. AnnouncedVersion: the last version whose changes were posted.
        public string MembersRoleId { get; set; }
        public string AnnouncementsChannelId { get; set; }
        public string AnnouncedVersion { get; set; }

        // QA and dev only: the admin-only QA checklist channel and its summary card (the items keep their own).
        public string QaChecklistChannelId { get; set; }
        public string QaChecklistSummaryMessageId { get; set; }

        // The bug reports channel members and admins read, and its one message with Report a problem.
        public string BugReportsChannelId { get; set; }
        public string BugReportsMessageId { get; set; }
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
