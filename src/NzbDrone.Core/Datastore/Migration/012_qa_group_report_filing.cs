using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the QA checklist's own channel group, and why a bug report isn't on GitHub yet, in words an admin can
    // act on.
    [Migration(12)]
    public class qa_group_report_filing : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("DiscordGuildConfigs").AddColumn("QaCategoryId").AsString().Nullable();
            Alter.Table("BugReports").AddColumn("FilingProblem").AsString().Nullable();
        }
    }
}
