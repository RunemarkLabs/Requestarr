using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: where each request's message and its admin approval card live, so both can be
    // edited to the request's real state after a decision. Older requests keep these empty.
    [Migration(4)]
    public class request_messages : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("MessageChannelId").AsString().Nullable();
            Alter.Table("Requests").AddColumn("MessageId").AsString().Nullable();
            Alter.Table("Requests").AddColumn("AdminMessageId").AsString().Nullable();
        }
    }
}
