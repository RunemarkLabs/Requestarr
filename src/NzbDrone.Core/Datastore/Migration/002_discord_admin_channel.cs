using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the admin channel (commands and approval cards) is tracked separately
    // from the admin queue channel the initial schema already had.
    [Migration(2)]
    public class discord_admin_channel : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DiscordGuildConfigs").AddColumn("AdminChannelId").AsString().Nullable();
        }
    }
}
