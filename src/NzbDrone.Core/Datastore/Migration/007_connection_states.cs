using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: each backend instance's connection state. FirstConnected empty: never connected
    // (its content type is hidden from users). DownSince set: lost; DownAlertMessageId is the admin
    // alert posted for that outage.
    [Migration(7)]
    public class connection_states : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Backends").AddColumn("FirstConnected").AsDateTimeOffset().Nullable();
            Alter.Table("Backends").AddColumn("DownSince").AsDateTimeOffset().Nullable();
            Alter.Table("Backends").AddColumn("DownAlertMessageId").AsString().Nullable();
        }
    }
}
