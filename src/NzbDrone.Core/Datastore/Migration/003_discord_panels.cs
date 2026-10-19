using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: pinned panel messages in each user channel and in the admin channel.
    [Migration(3)]
    public class discord_panels : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RequestarrUsers").AddColumn("PanelMessageId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("AdminPanelMessageId").AsString().Nullable();
        }
    }
}
