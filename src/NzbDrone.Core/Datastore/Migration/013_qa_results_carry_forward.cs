using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the build a QA checklist result was recorded on, so the summary can say how many passes were carried
    // over from an earlier build.
    [Migration(13)]
    public class qa_results_carry_forward : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QaChecklistItems").AddColumn("RecordedOn").AsString().Nullable();
        }
    }
}
