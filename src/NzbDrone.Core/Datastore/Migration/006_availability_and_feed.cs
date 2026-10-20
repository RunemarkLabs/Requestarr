using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: download progress and availability per request (seasons available so far for
    // TV), the request's card in the admin feed channel, and the feed channel itself.
    [Migration(6)]
    public class availability_and_feed : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("Progress").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("Requests").AddColumn("ProgressDetail").AsString().Nullable();
            Alter.Table("Requests").AddColumn("ProgressSizeLeft").AsInt64().Nullable();
            Alter.Table("Requests").AddColumn("ProgressSince").AsDateTimeOffset().Nullable();
            Alter.Table("Requests").AddColumn("AvailableSeasons").AsString().Nullable();
            Alter.Table("Requests").AddColumn("FeedMessageId").AsString().Nullable();
            Alter.Table("Requests").AddColumn("PosterUrl").AsString().Nullable();

            Alter.Table("DiscordGuildConfigs").AddColumn("FeedChannelId").AsString().Nullable();
            Alter.Table("DiscordGuildConfigs").AddColumn("FeedSeeded").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
