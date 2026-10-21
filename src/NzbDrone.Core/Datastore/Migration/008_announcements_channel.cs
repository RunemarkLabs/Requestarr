using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the announcements channel for every onboarded member (read-only for them) and the
    // shared members role that grants it; the last version whose changes were announced; and, per
    // backend outage, whether the admins were offered "Announce to users" and the post if they did.
    [Migration(8)]
    public class announcements_channel : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DiscordGuildConfigs").AddColumn("MembersRoleId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("AnnouncementsChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("AnnouncedVersion").AsString().Nullable();
            Alter.Table("Backends").AddColumn("DownAnnounceOffered").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("Backends").AddColumn("DownAnnounceMessageId").AsString().Nullable();
        }
    }
}
