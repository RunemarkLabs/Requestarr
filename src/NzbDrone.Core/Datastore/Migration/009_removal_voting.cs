using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the removal workflow. RemovalProposals and RemovalFlags exist since the first
    // migration; this adds what the vote needs (the title, the requester's prompt and answer, who
    // closed it) and one prompt per voter, reposted with the outcome.
    [Migration(9)]
    public class removal_voting : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RemovalProposals").AddColumn("Year").AsInt32().Nullable();
            Alter.Table("RemovalProposals").AddColumn("TmdbId").AsInt32().Nullable();
            Alter.Table("RemovalProposals").AddColumn("TvdbId").AsInt32().Nullable();
            Alter.Table("RemovalProposals").AddColumn("PosterUrl").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("InitiatedByName").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("RequesterUserId").AsInt32().Nullable();
            Alter.Table("RemovalProposals").AddColumn("Answered").AsDateTimeOffset().Nullable();
            Alter.Table("RemovalProposals").AddColumn("AnswerBy").AsDateTimeOffset().Nullable();
            Alter.Table("RemovalProposals").AddColumn("ReminderSent").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("RemovalProposals").AddColumn("RequesterChannelId").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("RequesterMessageId").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("VotingStarted").AsDateTimeOffset().Nullable();
            Alter.Table("RemovalProposals").AddColumn("ClosedByUserId").AsInt32().Nullable();
            Alter.Table("RemovalProposals").AddColumn("ClosedByName").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("Detail").AsString().Nullable();
            Alter.Table("RemovalProposals").AddColumn("FeedMessageId").AsString().Nullable();

            Create.TableForModel("RemovalPrompts")
                .WithColumn("ProposalId").AsInt32().NotNullable().Indexed()
                .WithColumn("UserId").AsInt32().NotNullable()
                .WithColumn("ChannelId").AsString().Nullable()
                .WithColumn("MessageId").AsString().Nullable()
                .WithColumn("Sent").AsDateTimeOffset().NotNullable();
        }
    }
}
