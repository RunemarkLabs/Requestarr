using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the sub/dub language of an anime request, and where each follower's message
    // lives so it can be reposted when the request changes.
    [Migration(5)]
    public class anime_and_followers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("Language").AsInt32().Nullable();
            Alter.Table("RequestSubscribers").AddColumn("MessageChannelId").AsString().Nullable();
            Alter.Table("RequestSubscribers").AddColumn("MessageId").AsString().Nullable();
        }
    }
}
